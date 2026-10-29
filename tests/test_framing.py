from pathlib import Path
import subprocess
import sys
import tempfile
from generate_fixture import generate

with tempfile.TemporaryDirectory() as directory:
    proto = Path(__file__).resolve().parent.parent / 'proto'
    subprocess.run(['protoc', '-I', str(proto), '--python_out=' + directory, str(proto / 'events.proto')], check=True)
    sys.path.insert(0, directory)
    from events_pb2 import EngineEvent
    fixture = Path(directory) / 'http.pcap'
    generate(fixture)
    process = subprocess.Popen([sys.argv[1], '--pcap', str(fixture)], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    counts = {'packet': 0, 'flow': 0}
    total_packets = 0
    while prefix := process.stdout.read(4):
        assert len(prefix) == 4
        size = int.from_bytes(prefix, 'big')
        assert 0 < size <= 1048576
        payload = process.stdout.read(size)
        assert len(payload) == size
        event = EngineEvent.FromString(payload)
        kind = event.WhichOneof('event')
        counts[kind] += 1
        if kind == 'flow':
            total_packets += event.flow.packets
        else:
            assert event.packet.src_ip == '192.0.2.1'
    assert process.wait(timeout=5) == 0, process.stderr.read()
    assert counts == {'packet': 3, 'flow': 2}, counts
    assert total_packets == 3
    broken = subprocess.Popen([sys.argv[1], '--pcap', str(fixture)], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    broken.stdout.close()
    assert broken.wait(timeout=5) == 0
print('PASS: 3 packet frames, 2 flow frames, protobuf contents, graceful EPIPE')
