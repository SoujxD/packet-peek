#include "peek/parser.h"

bool peek_ethernet(const uint8_t *data, size_t length, PeekEthernet *out)
{
    if (!data || !out || length < 14) return false;
    uint16_t type = (uint16_t)((uint16_t)data[12] << 8 | data[13]);
    if (type < 1536) return false;
    out->ether_type = type;
    out->header_length = 14;
    return true;
}
