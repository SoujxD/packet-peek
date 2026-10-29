#include "peek/flow.h"
#include <string.h>

size_t peek_key_hash(const PeekKey *key)
{
    size_t h = 2166136261u;
    for (size_t i = 0; i < 16; ++i) {
        h = (h ^ key->src[i]) * 16777619u;
        h = (h ^ key->dst[i]) * 16777619u;
    }
    h = (h ^ key->version) * 16777619u;
    h = (h ^ key->protocol) * 16777619u;
    h = (h ^ key->src_port) * 16777619u;
    return (h ^ key->dst_port) * 16777619u;
}

static bool equal(const PeekKey *a, const PeekKey *b)
{
    return a->version == b->version && a->protocol == b->protocol &&
           a->src_port == b->src_port && a->dst_port == b->dst_port &&
           memcmp(a->src, b->src, 16) == 0 && memcmp(a->dst, b->dst, 16) == 0;
}

PeekFlow *peek_flow_find(PeekTable *table, const PeekKey *key)
{
    size_t start = peek_key_hash(key) % PEEK_FLOW_CAPACITY;
    for (size_t n = 0; n < PEEK_FLOW_CAPACITY; ++n) {
        PeekSlot *slot = &table->slots[(start + n) % PEEK_FLOW_CAPACITY];
        if (slot->state == 0) return NULL;
        if (slot->state == 1 && equal(&slot->flow.key, key)) return &slot->flow;
    }
    return NULL;
}

bool peek_flow_add(PeekTable *table, const PeekKey *key, uint64_t timestamp_us, uint64_t bytes)
{
    PeekFlow *flow = peek_flow_find(table, key);
    if (!flow) {
        size_t start = peek_key_hash(key) % PEEK_FLOW_CAPACITY;
        for (size_t n = 0; n < PEEK_FLOW_CAPACITY; ++n) {
            PeekSlot *slot = &table->slots[(start + n) % PEEK_FLOW_CAPACITY];
            if (slot->state == 1) continue;
            slot->state = 1; slot->flow = (PeekFlow){.key = *key, .first_us = timestamp_us, .last_us = timestamp_us};
            ++table->count; flow = &slot->flow; break;
        }
        if (!flow) return false;
    }
    if (timestamp_us < flow->first_us) flow->first_us = timestamp_us;
    if (timestamp_us > flow->last_us) flow->last_us = timestamp_us;
    ++flow->packets; flow->bytes += bytes;
    return true;
}

bool peek_flow_sweep(PeekTable *table, uint64_t now_us, uint64_t idle_us, bool all,
                     PeekClose close, void *context)
{
    for (size_t i = 0; i < PEEK_FLOW_CAPACITY; ++i) {
        PeekSlot *slot = &table->slots[i];
        if (slot->state != 1) continue;
        if (!all && (now_us < slot->flow.last_us || now_us - slot->flow.last_us < idle_us)) continue;
        if (close && !close(&slot->flow, context)) return false;
        slot->state = 2; --table->count;
    }
    return true;
}
