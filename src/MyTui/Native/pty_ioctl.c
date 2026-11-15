#include <errno.h>
#include <fcntl.h>
#include <signal.h>
#include <unistd.h>
#include <sys/ioctl.h>
#include <sys/wait.h>
#include <termios.h>

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

static void fail_child(int errpipe, int err)
{
    int value = err;
    ssize_t wrote = write(errpipe, &value, sizeof value);
    (void)wrote;
    _exit(127);
}

/*
 * macOS does not make a tty the controlling terminal just because a session
 * leader opens it. The line discipline still echoes ^C, and the signal is
 * discarded because the pty has no foreground process group. Ctrl+C then
 * neither aborts a prompt line nor stops a running command.
 *
 * fork is confined to this file. The child calls only async-signal-safe
 * functions and then execs, so it never returns into the managed runtime.
 */
int mytui_spawn(int *pid, const char *file, const char *slave, const char *cwd, char **argv, char **envp)
{
    if (pid == 0 || file == 0 || slave == 0 || argv == 0 || envp == 0)
    {
        return EINVAL;
    }

    int pipefd[2];
    if (pipe(pipefd) < 0)
    {
        return errno;
    }

    if (fcntl(pipefd[1], F_SETFD, FD_CLOEXEC) < 0)
    {
        int err = errno;
        close(pipefd[0]);
        close(pipefd[1]);
        return err;
    }

    pid_t child = fork();
    if (child < 0)
    {
        int err = errno;
        close(pipefd[0]);
        close(pipefd[1]);
        return err;
    }

    if (child == 0)
    {
        close(pipefd[0]);
        sigset_t blocked;
        sigfillset(&blocked);
        sigprocmask(SIG_UNBLOCK, &blocked, 0);

        struct sigaction action;
        action.sa_handler = SIG_DFL;
        action.sa_flags = 0;
        sigemptyset(&action.sa_mask);
        for (int sig = 1; sig < NSIG; sig++)
        {
            if (sig == SIGKILL || sig == SIGSTOP)
            {
                continue;
            }

            sigaction(sig, &action, 0);
        }

        if (setsid() < 0)
        {
            fail_child(pipefd[1], errno);
        }

        if (cwd != 0 && cwd[0] != 0 && chdir(cwd) < 0)
        {
            fail_child(pipefd[1], errno);
        }

        int fd = open(slave, O_RDWR);
        if (fd < 0)
        {
            fail_child(pipefd[1], errno);
        }

        if (ioctl(fd, TIOCSCTTY, 0) < 0 && errno != EPERM)
        {
            fail_child(pipefd[1], errno);
        }

        if (tcsetpgrp(fd, getpid()) < 0)
        {
            fail_child(pipefd[1], errno);
        }

        if (fd != STDIN_FILENO && dup2(fd, STDIN_FILENO) < 0)
        {
            fail_child(pipefd[1], errno);
        }

        if (fd != STDOUT_FILENO && dup2(fd, STDOUT_FILENO) < 0)
        {
            fail_child(pipefd[1], errno);
        }

        if (fd != STDERR_FILENO && dup2(fd, STDERR_FILENO) < 0)
        {
            fail_child(pipefd[1], errno);
        }

        if (fd > STDERR_FILENO)
        {
            close(fd);
        }

        execve(file, argv, envp);
        fail_child(pipefd[1], errno);
    }

    close(pipefd[1]);
    int err = 0;
    for (;;)
    {
        ssize_t n = read(pipefd[0], &err, sizeof err);
        if (n < 0 && errno == EINTR)
        {
            continue;
        }

        close(pipefd[0]);
        if (n == 0)
        {
            *pid = (int)child;
            return 0;
        }

        int status = 0;
        while (waitpid(child, &status, 0) < 0 && errno == EINTR)
        {
        }

        return n < 0 ? errno : err;
    }
}
