#include <criterion/criterion.h>
#include "peek/parser.h"

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
