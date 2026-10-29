FROM debian:bookworm-slim AS native
RUN apt-get update && apt-get install -y --no-install-recommends build-essential cmake pkg-config libpcap-dev libprotobuf-c-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /source
COPY c c
RUN cmake -S c -B build -DBUILD_TESTING=OFF -DCMAKE_BUILD_TYPE=Release && cmake --build build

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS managed
WORKDIR /source
COPY proto proto
COPY dotnet/PacketPeek dotnet/PacketPeek
RUN dotnet publish dotnet/PacketPeek/PacketPeek.csproj -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim
USER root
RUN apt-get update && apt-get install -y --no-install-recommends libpcap0.8 libprotobuf-c1 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=managed /out .
COPY --from=native /source/build/libpeek /usr/local/bin/libpeek
COPY tests/fixtures/http.pcap /app/fixtures/http.pcap
RUN mkdir -p /app/logs && chown -R app:app /app
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PacketPeek.dll"]
