#include <criterion/criterion.h>

Test(smoke, framework_runs)
{
    cr_assert_eq(1 + 1, 2);
}
