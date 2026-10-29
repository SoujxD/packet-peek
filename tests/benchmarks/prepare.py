"""Keep complete records from an HTTP range prefix of bigFlows.pcap."""
from pathlib import Path
import hashlib
import json
import struct
import sys

source = Path(sys.argv[1]).read_bytes()
assert source[:4] in (b'\xd4\xc3\xb2\xa1', b'\xa1\xb2\xc3\xd4')
endian = '<' if source[:4] == b'\xd4\xc3\xb2\xa1' else '>'
offset = 24
packets = 0
while offset + 16 <= len(source):
    length = struct.unpack_from(endian + 'I', source, offset + 8)[0]
    assert length <= 65535
    if offset + 16 + length > len(source):
        break
    offset += 16 + length
    packets += 1
output = Path(sys.argv[2])
output.write_bytes(source[:offset])
print(json.dumps({'bytes': offset, 'packets': packets, 'sha256': hashlib.sha256(source[:offset]).hexdigest()}))
