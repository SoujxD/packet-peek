#ifndef PEEK_EMITTER_H
#define PEEK_EMITTER_H
#include "peek/capture.h"
#include "events.pb-c.h"
#include <stdio.h>
bool peek_write_frame(FILE *stream, const Peek__EngineEvent *event);
bool peek_emit_packet(const PeekKey *key, uint64_t timestamp_us, uint32_t bytes, void *stream);
bool peek_emit_flow(const PeekFlow *flow, void *stream);
#endif
