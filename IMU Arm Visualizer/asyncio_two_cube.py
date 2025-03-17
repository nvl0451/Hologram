import json
import numpy as np
import pyqtgraph as pg
import pyqtgraph.opengl as gl
from pyqtgraph.Qt import QtWidgets
from scipy.spatial.transform import Rotation as R
import asyncio
from asyncio_quaternion_server import AsyncQuaternionServer, DatagramHandler

# Initialize PyQtGraph application
app = QtWidgets.QApplication([])

# Create 3D view
view = gl.GLViewWidget()
view.show()
view.setWindowTitle('3D Quaternion Visualizer - Two Cubes')
view.setCameraPosition(distance=15)

# Add axis markers
axis = gl.GLAxisItem()
axis.setSize(x=5, y=5, z=5)
view.addItem(axis)

# Define cube vertices and faces
cube_vertices = np.array([
    [-0.5, -0.5, -0.5],
    [-0.5, -0.5,  0.5],
    [-0.5,  0.5, -0.5],
    [-0.5,  0.5,  0.5],
    [ 0.5, -0.5, -0.5],
    [ 0.5, -0.5,  0.5],
    [ 0.5,  0.5, -0.5],
    [ 0.5,  0.5,  0.5]
])
cube_faces = np.array([
    [0, 1, 3],
    [0, 3, 2],
    [4, 5, 7],
    [4, 7, 6],
    [0, 1, 5],
    [0, 5, 4],
    [2, 3, 7],
    [2, 7, 6],
    [0, 2, 6],
    [0, 6, 4],
    [1, 3, 7],
    [1, 7, 5]
])

# Define colors for the cubes
cube_left_color = np.array([[0, 1, 0, 0.8] for _ in range(len(cube_faces))])  # Green
cube_right_color = np.array([[0, 0, 1, 0.8] for _ in range(len(cube_faces))])  # Blue

# Create two cubes
cube_left = gl.GLMeshItem(vertexes=cube_vertices, faces=cube_faces, faceColors=cube_left_color, smooth=False)
cube_right = gl.GLMeshItem(vertexes=cube_vertices, faces=cube_faces, faceColors=cube_right_color, smooth=False)

# Add cubes to the 3D view
view.addItem(cube_left)
view.addItem(cube_right)

# Position the cubes in space
cube_left.translate(-2, 0, 0)  # Green cube on the left
cube_right.translate(2, 0, 0)  # Blue cube on the right

# Placeholder for quaternion data
quaternion_data = {
    "wrist": [1, 0, 0, 0],  # Identity quaternion
    "elbow": [1, 0, 0, 0],  # Identity quaternion
}


class VisualizerDatagramHandler(DatagramHandler):
    def __init__(self, ip_port_map, packet_rate, last_processed):
        super().__init__(ip_port_map, packet_rate, last_processed)

    def process_packet(self, sensor, data):
        """
        Update the quaternion_data dictionary with the latest data.
        """
        quaternion = [
            data.get("QW", 1),
            data.get("QX", 0),
            data.get("QY", 0),
            data.get("QZ", 0),
        ]
        quaternion_data[sensor] = quaternion


def update_cube(cube, quaternion, position, color):
    """
    Update a cube's orientation based on a quaternion and position.
    """
    # Convert quaternion to rotation matrix
    rotation = R.from_quat(quaternion)  # SciPy uses [x, y, z, w] order
    rotation_matrix = rotation.as_matrix()

    # Apply rotation to the cube vertices
    rotated_vertices = (rotation_matrix @ cube_vertices.T).T + position

    # Update the cube's mesh
    cube.setMeshData(vertexes=rotated_vertices, faces=cube_faces, faceColors=color, smooth=False)


def update_visualizer():
    """
    Update the cubes based on the latest quaternion data.
    """
    update_cube(cube_left, quaternion_data["wrist"], [-2, 0, 0], cube_left_color)  # Green cube
    update_cube(cube_right, quaternion_data["elbow"], [2, 0, 0], cube_right_color)  # Blue cube


# Timer for real-time updates
timer = pg.QtCore.QTimer()
timer.timeout.connect(update_visualizer)
timer.start(16)  # Approx. 60 FPS


async def main():
    """
    Start the asyncio server and listen for quaternion data.
    """
    # Define sensor-to-IP mapping
    ip_port_map = {
        "elbow": "192.168.1.159",
        "wrist": "192.168.1.160",
    }

    # Create the AsyncQuaternionServer and bind it to the custom DatagramHandler
    server = AsyncQuaternionServer(ip_port_map=ip_port_map)
    loop = asyncio.get_running_loop()
    transport, _ = await loop.create_datagram_endpoint(
        lambda: VisualizerDatagramHandler(server.ip_port_map, server.packet_rate, server.last_processed),
        local_addr=("0.0.0.0", server.port),
    )

    print("Server is running...")

    # Keep the server running indefinitely
    try:
        await asyncio.Future()  # Run forever
    except asyncio.CancelledError:
        print("Server shutting down.")
        transport.close()


# Run the asyncio server in a separate thread
import threading
thread = threading.Thread(target=lambda: asyncio.run(main()), daemon=True)
thread.start()

# Start the PyQt event loop
QtWidgets.QApplication.instance().exec()
