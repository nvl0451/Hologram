#include "Wire.h"
#include "I2Cdev.h"
#include "MPU6050_6Axis_MotionApps612.h"
#include <WiFi.h>
#include <WiFiUdp.h>

// Wi-Fi credentials
const char* ssid = "WiFire24GF9EAE7";
const char* password = "fSGg3X3A";

// Server settings
IPAddress serverIP(192, 168, 1, 157); // Replace with your server's IP
const int serverPort = 6942;

WiFiUDP udp;

// Assign sensor ID
String sensorID = "wrist"; // Change to "elbow" for the other ESP32

MPU6050 mpu(0x68);

void setup() {
    Wire.begin(5, 9); // SDA = GPIO5, SCL = GPIO9
    Wire.setClock(100000); // Set I2C clock to 100kHz

    Serial.begin(115200);
    delay(2000);

    Serial.println("Korolev Solutions 2025");
    Serial.println(String((char)toupper(sensorID[0])) + sensorID.substring(1) + " Hologram IMU Module Alpha 4 Quaternion Data Transmitter Initialize");

    // Connect to Wi-Fi
    Serial.println("Connecting to Wi-Fi...");
    WiFi.begin(ssid, password);
    while (WiFi.status() != WL_CONNECTED) {
        delay(500);
        Serial.print(".");
    }
    Serial.println("\nWi-Fi connected!");
    Serial.print("IP Address: ");
    Serial.println(WiFi.localIP());

    // Initialize MPU6050
    Serial.println("Initializing MPU6050...");
    mpu.initialize();

    // Verify connection
    if (!mpu.testConnection()) {
        Serial.println("MPU6050 connection failed!");
        while (1);
    }
    Serial.println("MPU6050 connected!");

    // Initialize DMP
    uint8_t error_code = mpu.dmpInitialize();
    if (error_code == 1) {
        Serial.println("DMP Initialization failed: Initial memory load failed.");
        while (1);
    }
    if (error_code == 2) {
        Serial.println("DMP Initialization failed: DMP configuration updates failed.");
        while (1);
    }

    // Calibrate accelerometer and gyroscope
    mpu.CalibrateAccel(6);
    mpu.CalibrateGyro(6);

    // Enable DMP
    mpu.setDMPEnabled(true);
    Serial.println("DMP ready!");
}

void loop() {
    uint8_t fifoBuffer[64]; // FIFO storage buffer

    // Poll the FIFO buffer for new data
    if (!mpu.dmpGetCurrentFIFOPacket(fifoBuffer)) {
        return; // No new data, exit loop early
    }

    // Read quaternion from the DMP
    Quaternion q;
    mpu.dmpGetQuaternion(&q, fifoBuffer);

    // Prepare quaternion data in JSON format
    String data = String("{") +
                  "\"sensor\":\"" + sensorID + "\"," +
                  "\"QW\":" + q.w + "," +
                  "\"QX\":" + q.x + "," +
                  "\"QY\":" + q.y + "," +
                  "\"QZ\":" + q.z + "}";

    // Send quaternion data via UDP
    if (udp.beginPacket(serverIP, serverPort)) {
        udp.print(data);
        udp.endPacket();
        Serial.println("Data sent: " + data);
    } else {
        Serial.println("Failed to send UDP packet!");
    }

    delay(16); // ~60 Hz
}
