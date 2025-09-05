CREATE TABLE portnums(
    id SERIAL PRIMARY KEY,
    portnum varchar,
    count INT,
    date TIMESTAMP DEFAULT NOW()
);

CREATE TABLE client(
    id SERIAL PRIMARY KEY,
    clientID varchar,
    long_name varchar,
    short_name varchar,
    date TIMESTAMP DEFAULT NOW()
);

CREATE TABLE messages(
    id SERIAL PRIMARY KEY,
    clientID varchar,
    message varchar,
    channel varchar,
    date TIMESTAMP DEFAULT NOW()
);

CREATE TABLE MQTTData(
    id SERIAL PRIMARY KEY,
    logged_at TIMESTAMP NOT NULL,
    uptime_seconds BIGINT NOT NULL
);

CREATE TABLE connections(
    id SERIAL PRIMARY KEY,
    clientID varchar,
    lastHeard TIMESTAMP DEFAULT NOW()
);

INSERT INTO MQTTData (id, logged_at, uptime_seconds)
VALUES (1, NOW(), 0)
    ON CONFLICT (id) DO NOTHING;