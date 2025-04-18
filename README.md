# Meshtastic MQTT Broker Boilerplate

This project provides an MQTT broker boilerplate specifically designed for Meshtastic mesh network moderation. It handles encrypted mesh packets, validates messages, and can be configured to run with SSL.

## Features

- MQTT server implementation for Meshtastic devices
- Allows for more precise access control than Mosquito ACLs
  - Support for encrypted mesh packet handling and validation
  - Support for validating client connections and subscriptions
- SSL support for secure MQTT connections
- Built using C# / .NET 9.0 with [MQTTnet](https://github.com/dotnet/MQTTnet)
- Multi-platform support
- Can be easily be packaged to run as a [portable standalone binary](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview?tabs=cli)
- Configurable logging with [Serilog](https://serilog.net/)

## Docker Setup

### Prerequisites

- Docker installed on your system
- Certificate file (if using SSL mode)

### Docker Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/meshtastic/mqtt
   cd mqtt
   ```

2. Build the Docker image:
   ```bash
   docker build -t meshtastic-mqtt-broker .
   ```

#### SSL Mode (Port 8883)

To run with SSL enabled:

1. Place your certificate file (`certificate.pfx`) in the project directory. (see [MQTTnet Server Wiki](https://github.com/dotnet/MQTTnet/wiki/Server))
2. Run the container with the SSL environment variable:

```bash
docker run -p 8883:8883 -v $(pwd)/certificate.pfx:/app/certificate.pfx meshtastic-mqtt-broker
```

### Docker Compose Example

```yaml
version: '3'
services:
  mqtt-broker:
    build: .
    ports:
      - "8883:8883"

    volumes:
      - ./certificate.pfx:/app/certificate.pfx
    restart: unless-stopped
```

## Configuration Options

- **Certificate**: Mount your PFX certificate file to `/app/certificate.pfx` in the container or preferably modify it in the parent folder after git cloning.
- **Ports**: The application uses  8883 for SSL MQTT (default).

## Ideas for MQTT mesh moderation

- Rate-limiting a packet we've heard before
- Rate-limiting packets per node
- "Zero hopping" certain packets
- Blocking unknown topics or undecryptable packets (from unknown channels)
- Blocking or rate-limiting certain portnums
- Fail2ban style connection moderation
- Banning from known bad actors list

## Troubleshooting

- Ensure proper network access to the Docker container
- Check that certificates are correctly formatted
- Review logs using `docker logs [container-id]`
