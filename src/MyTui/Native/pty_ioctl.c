#include <sys/ioctl.h>

/*
 * ioctl is variadic. On Apple arm64 those extra arguments are passed on the
 * stack, while a .NET P/Invoke leaves the pointer in a register. The kernel
 * then stores a garbage window size. This non-variadic wrapper is what clang
 * uses to spill the pointer before the real call.
 */
int mytui_ioctl_winsize(int fd, unsigned long request, void *size)
{
    return ioctl(fd, request, size);
}
