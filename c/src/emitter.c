#include "peek/emitter.h"
#include <arpa/inet.h>
#include <stdlib.h>

bool peek_write_frame(FILE *stream, const Peek__EngineEvent *event)
{
    size_t size = peek__engine_event__get_packed_size(event);
    if (!size || size > 1048576) return false;
    uint8_t *buffer = malloc(size);
    if (!buffer) return false;
    peek__engine_event__pack(event, buffer);
    uint8_t prefix[4] = {(uint8_t)(size >> 24), (uint8_t)(size >> 16), (uint8_t)(size >> 8), (uint8_t)size};
    bool ok = fwrite(prefix, 1, 4, stream) == 4 && fwrite(buffer, 1, size, stream) == size && fflush(stream) == 0;
    free(buffer); return ok;
}

static bool tuple(const PeekKey *key, Peek__PacketEvent *packet, char *src, char *dst)
{
    int family = key->version == 4 ? AF_INET : AF_INET6;
    if (!inet_ntop(family, key->src, src, INET6_ADDRSTRLEN) ||
        !inet_ntop(family, key->dst, dst, INET6_ADDRSTRLEN)) return false;
    packet->src_ip = src; packet->dst_ip = dst;
    packet->src_port = key->src_port; packet->dst_port = key->dst_port;
    packet->protocol = key->protocol;
    return true;
}

bool peek_emit_packet(const PeekKey *key, uint64_t timestamp_us, uint32_t bytes, void *stream)
{
    Peek__PacketEvent packet = PEEK__PACKET_EVENT__INIT;
    char src[INET6_ADDRSTRLEN], dst[INET6_ADDRSTRLEN];
    if (!tuple(key, &packet, src, dst)) return false;
    packet.timestamp_us = timestamp_us; packet.bytes = bytes;
    Peek__EngineEvent event = PEEK__ENGINE_EVENT__INIT;
    event.event_case = PEEK__ENGINE_EVENT__EVENT_PACKET; event.packet = &packet;
    return peek_write_frame(stream, &event);
}

bool peek_emit_flow(const PeekFlow *flow, void *stream)
{
    Peek__PacketEvent packet = PEEK__PACKET_EVENT__INIT;
    char src[INET6_ADDRSTRLEN], dst[INET6_ADDRSTRLEN];
    if (!tuple(&flow->key, &packet, src, dst)) return false;
    Peek__FlowSummary summary = PEEK__FLOW_SUMMARY__INIT;
    summary.tuple = &packet; summary.first_us = flow->first_us; summary.last_us = flow->last_us;
    summary.packets = flow->packets; summary.bytes = flow->bytes;
    Peek__EngineEvent event = PEEK__ENGINE_EVENT__INIT;
    event.event_case = PEEK__ENGINE_EVENT__EVENT_FLOW; event.flow = &summary;
    return peek_write_frame(stream, &event);
}
