# Meshtastic MQTT Broker Boilerplate

This project provides an MQTT broker boilerplate specifically designed for Meshtastic device networks. It handles encrypted mesh packets, validates messages, and can be configured to run with or without SSL.

## Features

- MQTT server implementation for Meshtastic devices
- Support for encrypted mesh packet handling and validation
- SSL support for secure MQTT connections
- Configurable logging with Serilog
- Packet filtering and validation logic

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
      - "1883:1883"  # Standard MQTT port
      # - "8883:8883"  # SSL port (uncomment if using SSL)
    # environment:
    #   - SSL=true  # Uncomment to enable SSL
    # volumes:
    #   - ./certificate.pfx:/app/certificate.pfx  # Mount certificate if using SSL
    restart: unless-stopped
```

## Configuration Options

- **SSL**: Set environment variable `SSL=true` to enable SSL mode
- **Certificate**: Mount your PFX certificate file to `/app/certificate.pfx` in the container
- **Ports**: The application uses port 1883 for standard MQTT and 8883 for SSL MQTT

## Troubleshooting

- Ensure proper network access to the Docker container
- Check that certificates are correctly formatted (for SSL mode)
- Review logs using `docker logs [container-id]`
