#include <criterion/criterion.h>
#include "peek/flow.h"

static bool count_close(const PeekFlow *flow, void *context)
{
    cr_assert_gt(flow->packets, 0);
    ++*(size_t *)context;
    return true;
}

Test(flow, collisions_tombstones_and_aging_boundary)
{
    PeekTable table = {0};
    PeekKey a = {.version = 4, .protocol = 6, .src_port = 1, .dst_port = 80}, b = a;
    do { ++b.src_port; } while (peek_key_hash(&a) % PEEK_FLOW_CAPACITY != peek_key_hash(&b) % PEEK_FLOW_CAPACITY);
    cr_assert(peek_flow_add(&table, &a, 100, 50));
    cr_assert(peek_flow_add(&table, &b, 150, 60));
    cr_assert(peek_flow_add(&table, &a, 110, 20));
    cr_assert_eq(peek_flow_find(&table, &a)->bytes, 70);
    size_t count = 0;
    cr_assert(peek_flow_sweep(&table, 209, 100, false, count_close, &count));
    cr_assert_eq(count, 0);
    cr_assert(peek_flow_sweep(&table, 210, 100, false, count_close, &count));
    cr_assert_eq(count, 1); cr_assert_null(peek_flow_find(&table, &a));
    cr_assert_not_null(peek_flow_find(&table, &b));
    cr_assert(peek_flow_add(&table, &a, 220, 80));
    cr_assert_eq(table.count, 2);
    cr_assert(peek_flow_sweep(&table, 0, 0, true, count_close, &count));
    cr_assert_eq(count, 3); cr_assert_eq(table.count, 0);
}

Test(flow, full_table_and_iteration_stability)
{
    PeekTable table = {0};
    PeekKey key = {.version = 6, .protocol = 17};
    for (size_t i = 0; i < PEEK_FLOW_CAPACITY; ++i) {
        key.src_port = (uint16_t)i;
        cr_assert(peek_flow_add(&table, &key, i, 1));
    }
    key.src_port = PEEK_FLOW_CAPACITY;
    cr_assert_not(peek_flow_add(&table, &key, 5000, 1));
    size_t count = 0;
    cr_assert(peek_flow_sweep(&table, 5000, 0, true, count_close, &count));
    cr_assert_eq(count, PEEK_FLOW_CAPACITY); cr_assert_eq(table.count, 0);
    cr_assert(peek_flow_add(&table, &key, 1, 2));
    cr_assert(peek_flow_sweep(&table, 0, 1, false, count_close, &count));
    cr_assert_eq(table.count, 1);
}
