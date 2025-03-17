import numpy as np
import pyqtgraph as pg
import pyqtgraph.opengl as gl
from pyqtgraph.Qt import QtWidgets, QtCore
from scipy.spatial.transform import Rotation as R
import asyncio
from asyncio_quaternion_server import AsyncQuaternionServer, DatagramHandler

# Initialize PyQtGraph application
app = QtWidgets.QApplication([])

# Create 3D view
view = gl.GLViewWidget()
view.show()
view.setWindowTitle("3D Arm Motion Visualizer")
view.setCameraPosition(distance=10)

# Add axis markers
axis = gl.GLAxisItem()
axis.setSize(x=5, y=5, z=5)
view.addItem(axis)

# Arm representation
# Upper arm (elbow to shoulder) as a line
upper_arm_line = gl.GLLinePlotItem()
view.addItem(upper_arm_line)

# Lower arm (wrist to elbow) as a line
lower_arm_line = gl.GLLinePlotItem()
view.addItem(lower_arm_line)

# Spheres to represent joints
def create_sphere(radius, color):
    sphere = gl.GLMeshItem(meshdata=gl.MeshData.sphere(rows=10, cols=10, radius=radius), color=color, smooth=True)
    return sphere

shoulder_sphere = create_sphere(0.2, (1, 0, 0, 1))  # Red sphere for shoulder
elbow_sphere = create_sphere(0.2, (0, 1, 0, 1))  # Green sphere for elbow
wrist_sphere = create_sphere(0.2, (0, 0, 1, 1))  # Blue sphere for wrist

view.addItem(shoulder_sphere)
view.addItem(elbow_sphere)
view.addItem(wrist_sphere)

# Placeholder for quaternion data
quaternion_data = {
    "wrist": [1, 0, 0, 0],  # Identity quaternion
    "elbow": [1, 0, 0, 0],  # Identity quaternion
}

# Reference quaternions for calibration
calibration_data = {
    "wrist": [1, 0, 0, 0],
    "elbow": [1, 0, 0, 0],
}

# Joint positions in 3D space
shoulder_position = np.array([0, 0, 0])  # Fixed
elbow_position = np.array([0, 2, 0])  # Initial elbow position
wrist_position = np.array([0, 4, 0])  # Initial wrist position

# Track sphere positions
shoulder_sphere_pos = shoulder_position.copy()
elbow_sphere_pos = elbow_position.copy()
wrist_sphere_pos = wrist_position.copy()


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


import copy

def calibrate_arm():
    """
    Capture the current quaternion state as the T-pose reference.
    """
    calibration_data["wrist"] = copy.deepcopy(quaternion_data["wrist"])
    calibration_data["elbow"] = copy.deepcopy(quaternion_data["elbow"])

    print("\n✅ Calibration Complete! Using current quaternions as T-pose reference.")
    print(f"🔹 Calibrated Elbow Quaternion: {calibration_data['elbow']}")
    print(f"🔹 Calibrated Wrist Quaternion: {calibration_data['wrist']}\n")



def multiplyQuaternion(q1, q0):
    """
    Multiply two quaternions.
    @param q1: Quaternion 1 in the form [w, x, y, z]
    @param q0: Quaternion 2 in the form [w, x, y, z]
    @return: Resultant quaternion in the form [w, x, y, z]
    """
    w0, x0, y0, z0 = q0
    w1, x1, y1, z1 = q1
    return np.array([
        -x1 * x0 - y1 * y0 - z1 * z0 + w1 * w0,
         x1 * w0 + y1 * z0 - z1 * y0 + w1 * x0,
        -x1 * z0 + y1 * w0 + z1 * x0 + w1 * y0,
         x1 * y0 - y1 * x0 + z1 * w0 + w1 * z0
    ], dtype=np.float64)


def create_translation_matrix(translation):
    """
    Create a 4x4 transformation matrix for translation.
    """
    matrix = np.eye(4, dtype=np.float32)
    matrix[:3, 3] = translation  # Set the translation values
    return matrix


def update_arm():
    """
    Update the arm segments and joint positions based on quaternion data using Blender-style logic.
    """
    global elbow_position, wrist_position

    # Get quaternion data and ensure it's a float64 array
    elbow_quaternion = np.array(quaternion_data["elbow"], dtype=np.float64)
    wrist_quaternion = np.array(quaternion_data["wrist"], dtype=np.float64)

    # Normalize quaternions (avoid division by zero)
    elbow_norm = np.linalg.norm(elbow_quaternion)
    wrist_norm = np.linalg.norm(wrist_quaternion)

    if elbow_norm > 0:
        elbow_quaternion /= elbow_norm
    if wrist_norm > 0:
        wrist_quaternion /= wrist_norm

    # Apply T-Pose Calibration (Reference Frame Correction)
    elbow_quaternion_inv = multiplyQuaternion(calibration_data["elbow"] * np.array([1, -1, -1, -1]), elbow_quaternion)
    wrist_quaternion_rel = multiplyQuaternion(multiplyQuaternion(calibration_data["elbow"] * np.array([1, -1, -1, -1]), elbow_quaternion),
                                              multiplyQuaternion(calibration_data["wrist"] * np.array([1, -1, -1, -1]), wrist_quaternion))

    # Lengths of arm segments
    upper_arm_length = 2.0  # Shoulder to Elbow
    lower_arm_length = 2.0  # Elbow to Wrist

    # Calculate the elbow's position relative to the shoulder
    rotation_elbow = R.from_quat(elbow_quaternion_inv).as_matrix()
    elbow_position = shoulder_position + rotation_elbow @ np.array([0, upper_arm_length, 0])

    # Calculate the wrist's position relative to the elbow
    rotation_wrist = R.from_quat(wrist_quaternion_rel).as_matrix()
    wrist_position = elbow_position + rotation_wrist @ np.array([0, lower_arm_length, 0])

    # Update the line segments for the arm
    upper_arm_line.setData(
        pos=np.array([shoulder_position, elbow_position]),
        color=(1, 1, 0, 1),  # Yellow
        width=2,
        mode="lines"
    )
    lower_arm_line.setData(
        pos=np.array([elbow_position, wrist_position]),
        color=(0, 1, 1, 1),  # Cyan
        width=2,
        mode="lines"
    )

    # Apply the corrected transformation matrices for the spheres
    shoulder_sphere.setTransform(create_translation_matrix(shoulder_position))
    elbow_sphere.setTransform(create_translation_matrix(elbow_position))
    wrist_sphere.setTransform(create_translation_matrix(wrist_position))





# Timer for real-time updates
timer = pg.QtCore.QTimer()
timer.timeout.connect(update_arm)
timer.start(16)  # Approx. 60 FPS

# Create a calibration button
calibrate_button = QtWidgets.QPushButton("Calibrate T-Pose")
calibrate_button.setFixedSize(150, 50)
calibrate_button.clicked.connect(calibrate_arm)

# Create a container window for the button
control_window = QtWidgets.QWidget()
control_layout = QtWidgets.QVBoxLayout()
control_layout.addWidget(calibrate_button)
control_window.setLayout(control_layout)
control_window.setWindowTitle("Controls")
control_window.show()


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
