#include <criterion/criterion.h>
#include "peek/emitter.h"

Test(framing, big_endian_prefix_and_protobuf_roundtrip)
{
    FILE *stream = tmpfile(); cr_assert_not_null(stream);
    PeekKey key = {.version = 4, .protocol = 6, .src_port = 12345, .dst_port = 80};
    key.src[0] = 192; key.dst[0] = 198;
    cr_assert(peek_emit_packet(&key, 1234567, 100, stream)); rewind(stream);
    uint8_t prefix[4], data[1024]; cr_assert_eq(fread(prefix, 1, 4, stream), 4);
    uint32_t size = (uint32_t)prefix[0] << 24 | (uint32_t)prefix[1] << 16 | (uint32_t)prefix[2] << 8 | prefix[3];
    cr_assert_gt(size, 0); cr_assert_lt(size, sizeof data);
    cr_assert_eq(fread(data, 1, size, stream), size); cr_assert_eq(fgetc(stream), EOF);
    Peek__EngineEvent *event = peek__engine_event__unpack(NULL, size, data);
    cr_assert_not_null(event); cr_assert_eq(event->event_case, PEEK__ENGINE_EVENT__EVENT_PACKET);
    cr_assert_eq(event->packet->timestamp_us, 1234567); cr_assert_eq(event->packet->bytes, 100);
    cr_assert_str_eq(event->packet->src_ip, "192.0.0.0");
    peek__engine_event__free_unpacked(event, NULL); fclose(stream);
}
