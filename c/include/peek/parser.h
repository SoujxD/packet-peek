#ifndef PEEK_PARSER_H
#define PEEK_PARSER_H
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef struct { uint16_t ether_type; size_t header_length; } PeekEthernet;
bool peek_ethernet(const uint8_t *data, size_t length, PeekEthernet *out);
typedef struct {
    uint8_t src[16], dst[16], protocol, version;
    size_t header_length, total_length;
} PeekIp;
bool peek_ipv4(const uint8_t *data, size_t length, PeekIp *out);
bool peek_ipv6(const uint8_t *data, size_t length, PeekIp *out);
#endif
