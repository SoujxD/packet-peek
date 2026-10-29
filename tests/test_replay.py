from pathlib import Path
import subprocess
import sys
import tempfile
from generate_fixture import generate

with tempfile.TemporaryDirectory() as directory:
    fixture = Path(directory) / 'http.pcap'
    generate(fixture)
    for bpf, counts in [('ip', 'packets=3 flows=2'), ('tcp port 80', 'packets=2 flows=1')]:
        result = subprocess.run([sys.argv[1], '--pcap', str(fixture), '--filter', bpf], capture_output=True)
        assert result.returncode == 0, result.stderr
        assert counts.encode() in result.stderr, result.stderr
        userspace = subprocess.run([sys.argv[1], '--pcap', str(fixture), '--filter', bpf, '--userspace-filter'], capture_output=True)
        assert userspace.returncode == 0, userspace.stderr
        assert counts.encode() in userspace.stderr, userspace.stderr
        assert userspace.stdout == result.stdout, 'BPF modes must emit identical events'
    invalid = subprocess.run([sys.argv[1], '--pcap', str(fixture), '--filter', '('], capture_output=True)
    assert invalid.returncode != 0
print('PASS: replay flow counts, BPF filtering, invalid BPF rejection')
