#include "peek/capture.h"
#include "peek/parser.h"
#include <pcap/pcap.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/time.h>

static volatile sig_atomic_t stopping;
static void stop(int sig) { (void)sig; stopping = 1; }
static uint64_t wall_us(void)
{
    struct timeval tv; gettimeofday(&tv, NULL);
    return (uint64_t)tv.tv_sec * 1000000u + (uint64_t)tv.tv_usec;
}

int peek_capture(const char *file, const char *interface_name, const char *filter,
                 uint64_t idle_us, PeekPacket packet, PeekClose close, void *context)
{
    char error[PCAP_ERRBUF_SIZE];
    pcap_t *pcap = file ? pcap_open_offline(file, error) : pcap_open_live(interface_name, 65535, 0, 100, error);
    if (!pcap) { fprintf(stderr, "pcap: %s\n", error); return 1; }
    if (pcap_datalink(pcap) != DLT_EN10MB) {
        fprintf(stderr, "only Ethernet II captures are supported\n"); pcap_close(pcap); return 1;
    }
    struct bpf_program program;
    if (pcap_compile(pcap, &program, filter, 1, PCAP_NETMASK_UNKNOWN) != 0) {
        fprintf(stderr, "BPF: %s\n", pcap_geterr(pcap)); pcap_close(pcap); return 1;
    }
    int installed = pcap_setfilter(pcap, &program); pcap_freecode(&program);
    if (installed != 0) { fprintf(stderr, "BPF: %s\n", pcap_geterr(pcap)); pcap_close(pcap); return 1; }
    if (!file && pcap_setnonblock(pcap, 1, error) != 0) {
        fprintf(stderr, "pcap nonblock: %s\n", error); pcap_close(pcap); return 1;
    }
    PeekTable *table = calloc(1, sizeof *table);
    if (!table) { pcap_close(pcap); return 1; }
    stopping = 0; signal(SIGINT, stop); signal(SIGTERM, stop);
    uint64_t now = 0, packets = 0; size_t flows = 0; int result = 0;
    while (!stopping) {
        struct pcap_pkthdr *header; const u_char *data;
        int status = pcap_next_ex(pcap, &header, &data);
        if (status == -2) break;
        if (status == -1) { fprintf(stderr, "pcap read: %s\n", pcap_geterr(pcap)); result = 1; break; }
        uint64_t timestamp = status == 1 ? (uint64_t)header->ts.tv_sec * 1000000u + (uint64_t)header->ts.tv_usec : wall_us();
        if (timestamp > now) now = timestamp;
        size_t before = table->count;
        if (!peek_flow_sweep(table, now, idle_us, false, close, context)) { result = 1; break; }
        flows += before - table->count;
        if (status == 0) { struct timeval delay = {.tv_sec = 0, .tv_usec = 10000}; select(0, NULL, NULL, NULL, &delay); continue; }
        PeekEthernet eth; PeekIp ip; PeekTransport transport;
        if (!peek_ethernet(data, header->caplen, &eth)) continue;
        data += eth.header_length; size_t length = header->caplen - eth.header_length;
        if (eth.ether_type == 0x0800) { if (!peek_ipv4(data, length, &ip)) continue; }
        else if (eth.ether_type == 0x86dd) { if (!peek_ipv6(data, length, &ip)) continue; }
        else continue;
        data += ip.header_length; length = ip.total_length - ip.header_length;
        if (ip.protocol == 6) { if (!peek_tcp(data, length, &transport)) continue; }
        else if (ip.protocol == 17) { if (!peek_udp(data, length, &transport)) continue; }
        else continue;
        PeekKey key = {.version = ip.version, .protocol = ip.protocol,
                       .src_port = transport.src_port, .dst_port = transport.dst_port};
        memcpy(key.src, ip.src, 16); memcpy(key.dst, ip.dst, 16);
        if (!peek_flow_add(table, &key, timestamp, header->len)) {
            fprintf(stderr, "flow table full\n"); result = 1; break;
        }
        if (packet && !packet(&key, timestamp, header->len, context)) { result = 1; break; }
        ++packets;
    }
    flows += table->count;
    if (!result && !peek_flow_sweep(table, now, idle_us, true, close, context)) result = 1;
    fprintf(stderr, "packets=%llu flows=%zu\n", (unsigned long long)packets, flows);
    free(table); pcap_close(pcap); return result;
}
