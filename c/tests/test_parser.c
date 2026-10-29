#include <criterion/criterion.h>
#include "peek/parser.h"

Test(ipv6, decodes_base_header_and_checks_payload_bounds)
{
    uint8_t data[48] = {0x60, 0, 0, 0, 0, 8, 17, 64};
    data[8] = 0x20; data[23] = 1; data[24] = 0x20; data[39] = 2;
    PeekIp out;
    cr_assert(peek_ipv6(data, 48, &out));
    cr_assert_eq(out.version, 6); cr_assert_eq(out.protocol, 17);
    cr_assert_eq(out.header_length, 40); cr_assert_eq(out.total_length, 48);
    cr_assert_eq(out.src[15], 1); cr_assert_eq(out.dst[15], 2);
    for (size_t n = 0; n < 48; ++n) cr_assert_not(peek_ipv6(data, n, &out));
    data[0] = 0x40; cr_assert_not(peek_ipv6(data, 48, &out));
    data[0] = 0x60; data[5] = 9; cr_assert_not(peek_ipv6(data, 48, &out));
}

Test(ipv4, validates_lengths_version_and_fragments)
{
    uint8_t data[24] = {0x46, 0, 0, 24, 0, 0, 0, 0, 64, 6};
    data[12] = 192; data[15] = 1; data[16] = 10; data[19] = 2;
    PeekIp out;
    cr_assert(peek_ipv4(data, sizeof data, &out));
    cr_assert_eq(out.header_length, 24); cr_assert_eq(out.total_length, 24);
    cr_assert_eq(out.src[0], 192); cr_assert_eq(out.dst[3], 2);
    cr_assert_eq(out.protocol, 6); cr_assert_eq(out.version, 4);
    for (size_t n = 0; n < 24; ++n) cr_assert_not(peek_ipv4(data, n, &out));
    data[0] = 0x44; cr_assert_not(peek_ipv4(data, 24, &out));
    data[0] = 0x65; cr_assert_not(peek_ipv4(data, 24, &out));
    data[0] = 0x46; data[3] = 20; cr_assert_not(peek_ipv4(data, 24, &out));
    data[3] = 24; data[6] = 0x20; cr_assert_not(peek_ipv4(data, 24, &out));
    data[6] = 0; data[7] = 1; cr_assert_not(peek_ipv4(data, 24, &out));
}

Test(ethernet, decodes_type_and_rejects_truncation_and_length_fields)
{
    uint8_t data[14] = {0};
    data[12] = 0x86; data[13] = 0xdd;
    PeekEthernet out;
    cr_assert(peek_ethernet(data, sizeof data, &out));
    cr_assert_eq(out.ether_type, 0x86dd);
    cr_assert_eq(out.header_length, 14);
    for (size_t n = 0; n < 14; ++n) cr_assert_not(peek_ethernet(data, n, &out));
    data[12] = 0; data[13] = 42;
    cr_assert_not(peek_ethernet(data, sizeof data, &out));
}
