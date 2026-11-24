FROM golang:1.25-alpine AS healthcheck
WORKDIR /src
COPY healthcheck/main.go .
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /healthcheck main.go

FROM grafana/alloy:v1.21.3
COPY --from=healthcheck /healthcheck /usr/local/bin/healthcheck
