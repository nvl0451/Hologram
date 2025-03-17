import asyncio
import json
import time


class AsyncQuaternionServer:
    def __init__(self, ip_port_map, port=6942):
        self.ip_port_map = ip_port_map  # e.g., {"elbow": "192.168.1.164", "wrist": "192.168.1.165"}
        self.port = port
        self.packet_rate = {sensor: 0 for sensor in ip_port_map}  # Track incoming packets
        self.last_processed = {sensor: time.time() for sensor in ip_port_map}  # Throttling

    async def listen(self):
        print(f"Server starting on port {self.port}")
        loop = asyncio.get_running_loop()
        transport, protocol = await loop.create_datagram_endpoint(
            lambda: DatagramHandler(self.ip_port_map, self.packet_rate, self.last_processed),
            local_addr=("0.0.0.0", self.port),
        )
        try:
            await asyncio.Future()  # Run the server indefinitely
        except asyncio.CancelledError:
            print("Server shutting down.")
            transport.close()

    async def monitor_packet_rate(self):
        """
        Periodically print packet rates for each sensor.
        """
        while True:
            await asyncio.sleep(5)
            print("\nPacket Rates (packets/sec):")
            for sensor, count in self.packet_rate.items():
                print(f"{sensor}: {count / 5:.2f}")
            self.packet_rate = {sensor: 0 for sensor in self.ip_port_map}  # Reset packet counts


class DatagramHandler(asyncio.DatagramProtocol):
    def __init__(self, ip_port_map, packet_rate, last_processed):
        self.ip_port_map = ip_port_map
        self.packet_rate = packet_rate
        self.last_processed = last_processed

    def connection_made(self, transport):
        print("Connection established.")
        self.transport = transport

    def datagram_received(self, data, addr):
        ip, _ = addr
        for sensor, sensor_ip in self.ip_port_map.items():
            if ip == sensor_ip:
                try:
                    # Increment packet counter
                    self.packet_rate[sensor] += 1

                    # Optional: Throttle packet processing (e.g., 100 Hz max)
                    now = time.time()
                    if now - self.last_processed[sensor] < 0.01:  # 10ms interval
                        return

                    self.last_processed[sensor] = now

                    # Parse incoming data
                    parsed_data = json.loads(data.decode("utf-8"))
                    print(f"[{sensor}] Received packet: {parsed_data}")
                    # Process the packet (replace this with actual processing logic)
                    self.process_packet(sensor, parsed_data)
                except json.JSONDecodeError as e:
                    print(f"Malformed data from {ip}: {data} ({e})")
                except Exception as e:
                    print(f"Unexpected error processing data from {ip}: {e}")
                break

    def process_packet(self, sensor, data):
        """
        Simulate packet processing. Replace this with actual logic.
        """
        print(f"[{sensor}] Processing data: {data}")

    def connection_lost(self, exc):
        print("Connection lost.")
        if exc:
            print(f"Connection error: {exc}")


# Main function
async def main():
    # Define sensor-to-IP mapping
    ip_port_map = {
        "elbow": "192.168.1.164",
        "wrist": "192.168.1.165",
    }

    # Initialize the server
    server = AsyncQuaternionServer(ip_port_map=ip_port_map)

    # Start the server and monitor packet rates
    server_task = asyncio.create_task(server.listen())
    monitor_task = asyncio.create_task(server.monitor_packet_rate())

    try:
        await asyncio.gather(server_task, monitor_task)
    except asyncio.CancelledError:
        print("Main event loop stopped.")


if __name__ == "__main__":
    asyncio.run(main())
