#ifndef PEEK_PARSER_H
#define PEEK_PARSER_H
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef struct { uint16_t ether_type; size_t header_length; } PeekEthernet;
bool peek_ethernet(const uint8_t *data, size_t length, PeekEthernet *out);
#endif
