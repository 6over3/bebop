#include "../gen/union.h"
#include "../gen/bebop.h"
#include <assert.h>
#include <stdio.h>

int main()
{
    // Create WeirdOrder union
    weird_order_t o;

    // We can put a value in...
    // Set tag for TwoComesFirst (discriminator value 2 based on the name)
    o.tag = 2;
    o.as_two_comes_first.b = 42;

    // And get it back out
    if (o.tag == 2) { // Check if it's TwoComesFirst
        two_comes_first_t x = o.as_two_comes_first;
        if (x.b != 42)
            return 1;
    } else {
        return 1; // Wrong tag
    }

    return 0;
}