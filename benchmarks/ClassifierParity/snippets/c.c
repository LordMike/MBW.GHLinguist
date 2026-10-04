#include <stdio.h>
#include <stdlib.h>
static int add(int a, int b) { return a + b; }
int main(void) {
    printf("%d\n", add(1, 2));
    return EXIT_SUCCESS;
}
