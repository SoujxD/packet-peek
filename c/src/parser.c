#include "peek/parser.h"
#include <string.h>

static uint16_t be16(const uint8_t *p) { return (uint16_t)((uint16_t)p[0] << 8 | p[1]); }

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
