#ifndef PEEK_FLOW_H
#define PEEK_FLOW_H
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#define PEEK_FLOW_CAPACITY 4096
typedef struct {
    uint8_t src[16], dst[16], version, protocol;
    uint16_t src_port, dst_port;
} PeekKey;
typedef struct {
    PeekKey key;
    uint64_t first_us, last_us, packets, bytes;
} PeekFlow;
typedef struct { unsigned char state; PeekFlow flow; } PeekSlot;
typedef struct { PeekSlot slots[PEEK_FLOW_CAPACITY]; size_t count; } PeekTable;
typedef bool (*PeekClose)(const PeekFlow *flow, void *context);
size_t peek_key_hash(const PeekKey *key);
PeekFlow *peek_flow_find(PeekTable *table, const PeekKey *key);
bool peek_flow_add(PeekTable *table, const PeekKey *key, uint64_t timestamp_us, uint64_t bytes);
bool peek_flow_sweep(PeekTable *table, uint64_t now_us, uint64_t idle_us, bool all,
                     PeekClose close, void *context);
#endif
