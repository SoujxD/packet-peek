#include "peek/parser.h"
#include <string.h>

static uint16_t be16(const uint8_t *p) { return (uint16_t)((uint16_t)p[0] << 8 | p[1]); }

bool peek_udp(const uint8_t *data, size_t length, PeekTransport *out)
{
    if (!data || !out || length < 8) return false;
    size_t total = be16(data + 4);
    if (total < 8 || total > length) return false;
    out->src_port = be16(data); out->dst_port = be16(data + 2);
    out->header_length = 8;
    return true;
}

bool peek_tcp(const uint8_t *data, size_t length, PeekTransport *out)
{
    if (!data || !out || length < 20) return false;
    size_t header = (data[12] >> 4) * 4u;
    if (header < 20 || header > length) return false;
    out->src_port = be16(data); out->dst_port = be16(data + 2);
    out->header_length = header;
    return true;
}

bool peek_ipv6(const uint8_t *data, size_t length, PeekIp *out)
{
    if (!data || !out || length < 40 || data[0] >> 4 != 6) return false;
    size_t total = 40u + be16(data + 4);
    if (total > length) return false;
    memset(out, 0, sizeof *out);
    memcpy(out->src, data + 8, 16); memcpy(out->dst, data + 24, 16);
    out->version = 6; out->protocol = data[6];
    out->header_length = 40; out->total_length = total;
    return true;
}

bool peek_ipv4(const uint8_t *data, size_t length, PeekIp *out)
{
    if (!data || !out || length < 20 || data[0] >> 4 != 4) return false;
    size_t header = (data[0] & 15) * 4u, total = be16(data + 2);
    if (header < 20 || header > length || total < header || total > length ||
        (be16(data + 6) & 0x3fff)) return false;
    memset(out, 0, sizeof *out);
    memcpy(out->src, data + 12, 4); memcpy(out->dst, data + 16, 4);
    out->version = 4; out->protocol = data[9];
    out->header_length = header; out->total_length = total;
    return true;
}

bool peek_ethernet(const uint8_t *data, size_t length, PeekEthernet *out)
{
    if (!data || !out || length < 14) return false;
    uint16_t type = (uint16_t)((uint16_t)data[12] << 8 | data[13]);
    if (type < 1536) return false;
    out->ether_type = type;
    out->header_length = 14;
    return true;
}
