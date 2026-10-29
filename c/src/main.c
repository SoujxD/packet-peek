#include "peek/capture.h"
#include "peek/emitter.h"
#include <errno.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int main(int argc, char **argv)
{
    const char *file = NULL, *interface_name = NULL, *filter = "ip or ip6";
    unsigned long idle = 30;
    bool userspace_filter = false;
    for (int i = 1; i < argc; ++i) {
        if (!strcmp(argv[i], "--userspace-filter")) { userspace_filter = true; continue; }
        if (i + 1 >= argc) goto usage;
        const char *value = argv[++i];
        if (!strcmp(argv[i - 1], "--pcap")) file = value;
        else if (!strcmp(argv[i - 1], "--interface")) interface_name = value;
        else if (!strcmp(argv[i - 1], "--filter")) filter = value;
        else if (!strcmp(argv[i - 1], "--idle-seconds")) {
            char *end; errno = 0; idle = strtoul(value, &end, 10);
            if (errno || *end || !idle || idle > 86400 || *value == '-') goto usage;
        } else goto usage;
    }
    if ((!file && !interface_name) || (file && interface_name)) goto usage;
    signal(SIGPIPE, SIG_IGN);
    int result = peek_capture(file, interface_name, filter, (uint64_t)idle * 1000000u, userspace_filter,
                              peek_emit_packet, peek_emit_flow, stdout);
    if (result && errno == EPIPE) { fprintf(stderr, "stdout closed (EPIPE), stopping\n"); return 0; }
    return result;
usage:
    fprintf(stderr, "usage: libpeek (--pcap FILE | --interface NAME) [--filter BPF] [--idle-seconds N] [--userspace-filter]\n");
    return 2;
}
