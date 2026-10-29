#include <criterion/criterion.h>
#include <pcap/pcap.h>
#include "peek/parser.h"

Test(pcap_fixture, ethernet_ip_and_transport_fields_match_real_records)
{
    char error[PCAP_ERRBUF_SIZE];
    pcap_t *capture = pcap_open_offline(PEEK_FIXTURE_PATH, error);
    cr_assert_not_null(capture, "%s", error);
    unsigned tcp = 0, udp = 0;
    struct pcap_pkthdr *header; const u_char *data;
    int status;
    while ((status = pcap_next_ex(capture, &header, &data)) == 1) {
        PeekEthernet eth; PeekIp ip; PeekTransport transport;
        cr_assert(peek_ethernet(data, header->caplen, &eth));
        cr_assert_eq(eth.ether_type, 0x0800);
        cr_assert(peek_ipv4(data + 14, header->caplen - 14, &ip));
        cr_assert_eq(ip.src[0], 192); cr_assert_eq(ip.src[3], 1);
        cr_assert_eq(ip.dst[0], 198); cr_assert_eq(ip.dst[3], 2);
        data += 14 + ip.header_length;
        size_t length = ip.total_length - ip.header_length;
        if (ip.protocol == 6) {
            cr_assert(peek_tcp(data, length, &transport)); cr_assert_eq(transport.dst_port, 80); ++tcp;
        } else {
            cr_assert_eq(ip.protocol, 17); cr_assert(peek_udp(data, length, &transport));
            cr_assert_eq(transport.dst_port, 53); ++udp;
        }
        cr_assert_eq(transport.src_port, 12345);
    }
    cr_assert_eq(status, -2); cr_assert_eq(tcp, 2); cr_assert_eq(udp, 1);
    pcap_close(capture);
}
