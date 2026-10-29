#ifndef PEEK_CAPTURE_H
#define PEEK_CAPTURE_H
#include "peek/flow.h"
typedef bool (*PeekPacket)(const PeekKey *, uint64_t timestamp_us, uint32_t bytes, void *context);
int peek_capture(const char *pcap_file, const char *interface_name, const char *filter,
                 uint64_t idle_us, PeekPacket packet, PeekClose close, void *context);
#endif
