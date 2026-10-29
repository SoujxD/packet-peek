"""Generate a deterministic Ethernet/IPv4 pcap; no external capture tools required."""
from pathlib import Path
import struct


def checksum(data):
    total = sum(struct.unpack('!10H', data))
    while total >> 16:
        total = (total & 65535) + (total >> 16)
    return ~total & 65535


def generate(path):
    packets = []
    for protocol, port, payload in [(6, 80, b'GET / HTTP/1.0\r\n\r\n'), (6, 80, b''), (17, 53, b'test')]:
        transport = (struct.pack('!HHIIBBHHH', 12345, port, 1, 0, 0x50, 0x18, 8192, 0, 0)
                     if protocol == 6 else struct.pack('!HHHH', 12345, port, 8 + len(payload), 0))
        ip = struct.pack('!BBHHHBBH4s4s', 0x45, 0, 20 + len(transport) + len(payload), 1, 0x4000,
                         64, protocol, 0, b'\xc0\x00\x02\x01', b'\xc6\x33\x64\x02')
        ip = ip[:10] + struct.pack('!H', checksum(ip)) + ip[12:]
        packet = bytes.fromhex('00112233445566778899aabb0800') + ip + transport + payload
        packets.append(packet)
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('wb') as output:
        output.write(struct.pack('<IHHIIII', 0xa1b2c3d4, 2, 4, 0, 0, 65535, 1))
        for index, packet in enumerate(packets):
            output.write(struct.pack('<IIII', 1700000000, index * 100000, len(packet), len(packet)))
            output.write(packet)


if __name__ == '__main__':
    generate(Path(__file__).parent / 'fixtures/http.pcap')
