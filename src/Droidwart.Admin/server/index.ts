import express, { type NextFunction, type Request, type Response } from "express";
import multer from "multer";
import { WebSocketServer } from "ws";
import { createServer } from "node:http";
import { resolve } from "node:path";
import { readFile } from "node:fs/promises";
import { AdbServerClient } from "@yume-chan/adb";
import { AdbServerNodeTcpConnector } from "@yume-chan/adb-server-node-tcp";
import { AdbScrcpyClient, AdbScrcpyOptionsLatest } from "@yume-chan/adb-scrcpy";
import { ReadableStream } from "@yume-chan/stream-extra";
import {
  AndroidMotionEventAction,
  AndroidMotionEventButton,
  ScrcpyPointerId,
} from "@yume-chan/scrcpy";

const adbHost = process.env.ADB_SERVER_HOST ?? "127.0.0.1";
const adbPort = Number(process.env.ADB_SERVER_PORT ?? "5037");
const port = Number(process.env.PORT ?? "4173");
const scrcpyServer = resolve(process.env.SCRCPY_SERVER ?? "scrcpy-server-v3.3.3");

const connector = new AdbServerNodeTcpConnector({ host: adbHost, port: adbPort });
const adbServer = new AdbServerClient(connector);
const app = express();
const upload = multer({ storage: multer.memoryStorage(), limits: { fileSize: 512 * 1024 * 1024 } });

app.use(express.json());
app.use(express.static(resolve("dist")));

type AppAction = "stop" | "restart";

const allowedSettings = new Map<string, string>([
  ["stay_on_while_plugged", "stay_on_while_plugged_in"],
  ["screen_off_timeout", "screen_off_timeout"],
  ["bluetooth_on", "bluetooth_on"],
  ["wifi_on", "wifi_on"],
]);

function validateSerial(value: unknown): asserts value is string {
  if (typeof value !== "string" || !/^[-A-Za-z0-9.:]+$/.test(value)) {
    throw new Error("Invalid device serial.");
  }
}

async function getAdb(serial: string) {
  const device = (await adbServer.getDevices(["unauthorized", "device", "offline"]))
    .find(candidate => candidate.serial === serial);
  if (!device) {
    throw new Error("Device is not known to the ADB server.");
  }
  if (device.state !== "device") {
    throw new Error(`Device is ${device.state}.`);
  }
  return adbServer.createAdb(device);
}

async function runShell(adb: Awaited<ReturnType<typeof getAdb>>, command: readonly string[]) {
  if (adb.subprocess.shellProtocol) {
    return adb.subprocess.shellProtocol.spawnWaitText(command);
  }
  const output = await adb.subprocess.noneProtocol.spawnWaitText(command);
  return { stdout: output, stderr: "", exitCode: 0 };
}

app.get("/api/devices", async (_request, response, next) => {
  try {
    const devices = await adbServer.getDevices(["unauthorized", "device", "offline"]);
    response.json(devices.map(device => ({
      serial: device.serial,
      state: device.state,
      model: device.model,
      product: device.product,
      device: device.device,
      transportId: device.transportId.toString(),
    })));
  } catch (error) {
    next(error);
  }
});

app.post("/api/devices/:serial/apps/:package/start", async (request, response, next) => {
  try {
    validateSerial(request.params.serial);
    const packageName = String(request.params.package);
    if (!/^[-A-Za-z0-9_.]+$/.test(packageName)) {
      throw new Error("Invalid Android package name.");
    }
    const adb = await getAdb(request.params.serial);
    const result = await runShell(adb, ["monkey", "-p", packageName, "-c", "android.intent.category.LAUNCHER", "1"]);
    response.json({ ok: result.exitCode === 0, result });
  } catch (error) {
    next(error);
  }
});

app.post("/api/devices/:serial/apps/:package/:action", async (request, response, next) => {
  try {
    validateSerial(request.params.serial);
    const action = request.params.action as AppAction;
    const packageName = String(request.params.package);
    if (!["stop", "restart"].includes(action) || !/^[-A-Za-z0-9_.]+$/.test(packageName)) {
      throw new Error("Invalid app action or package name.");
    }
    const adb = await getAdb(request.params.serial);
    const stopped = await runShell(adb, ["am", "force-stop", packageName]);
    if (action === "restart") {
      const launched = await runShell(adb, ["monkey", "-p", packageName, "-c", "android.intent.category.LAUNCHER", "1"]);
      response.json({ ok: stopped.exitCode === 0 && launched.exitCode === 0, stopped, launched });
    } else {
      response.json({ ok: stopped.exitCode === 0, stopped });
    }
  } catch (error) {
    next(error);
  }
});

app.post("/api/devices/:serial/settings/:name", async (request, response, next) => {
  try {
    validateSerial(request.params.serial);
    const settingName = allowedSettings.get(request.params.name);
    const value = String(request.body?.value ?? "");
    if (!settingName || !/^[0-9A-Za-z_-]+$/.test(value)) {
      throw new Error("Setting or value is not allowlisted.");
    }
    const adb = await getAdb(request.params.serial);
    const namespace = settingName === "bluetooth_on" || settingName === "wifi_on" ? "global" : "system";
    const result = await runShell(adb, ["settings", "put", namespace, settingName, value]);
    response.json({ ok: result.exitCode === 0, result });
  } catch (error) {
    next(error);
  }
});

app.post("/api/devices/:serial/apps/install", upload.single("apk"), async (request, response, next) => {
  try {
    validateSerial(request.params.serial);
    if (!request.file) {
      throw new Error("No APK file was uploaded.");
    }
    if (!request.file.originalname.toLowerCase().endsWith(".apk")) {
      throw new Error("Only APK files are accepted.");
    }
    const adb = await getAdb(request.params.serial);
    const sync = await adb.sync();
    const remotePath = `/data/local/tmp/droidwart-${Date.now()}.apk`;
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(request.file!.buffer);
        controller.close();
      },
    });
    await sync.write({ filename: remotePath, file: stream as never });
    await sync.dispose();

    const replace = request.body?.replace === "true";
    const grant = request.body?.grant === "true";
    const argumentsList = ["pm", "install", ...(replace ? ["-r"] : []), ...(grant ? ["-g"] : []), "-t", remotePath];
    const result = await runShell(adb, argumentsList);
    await runShell(adb, ["rm", "-f", remotePath]);
    response.json({ ok: result.exitCode === 0, result });
  } catch (error) {
    next(error);
  }
});

app.use((error: unknown, _request: Request, response: Response, _next: NextFunction) => {
  const message = error instanceof Error ? error.message : "Unexpected error.";
  response.status(400).json({ error: message });
});

const server = createServer(app);
const sockets = new WebSocketServer({ noServer: true });

server.on("upgrade", (request, socket, head) => {
  const path = new URL(request.url ?? "/", "http://localhost").pathname;
  if (!path.startsWith("/ws/scrcpy/")) {
    socket.destroy();
    return;
  }
  sockets.handleUpgrade(request, socket, head, ws => sockets.emit("connection", ws, request));
});

sockets.on("connection", async (socket, request) => {
  const path = new URL(request.url ?? "/", "http://localhost").pathname;
  const serial = decodeURIComponent(path.slice("/ws/scrcpy/".length));

  try {
    validateSerial(serial);
    const adb = await getAdb(serial);
    const serverFile = await readFile(scrcpyServer);
    await AdbScrcpyClient.pushServer(
      adb,
      new ReadableStream({
        start(controller) {
          controller.enqueue(new Uint8Array(serverFile));
          controller.close();
        },
      }),
    );

    const options = new AdbScrcpyOptionsLatest({
      tunnelForward: false,
      video: true,
      audio: false,
      control: true,
      maxSize: 1280,
      videoBitRate: 8_000_000,
    });
    const client = await AdbScrcpyClient.start(adb, "/data/local/tmp/scrcpy-server.jar", options);
    const video = await client.videoStream;
    const reader = video.stream.getReader();
    socket.send(JSON.stringify({
      type: "ready",
      serial,
      width: video.width,
      height: video.height,
    }));

    const videoTask = (async () => {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        socket.send(JSON.stringify({
          type: "video",
          packetType: value.type,
          keyframe: value.type === "data" ? value.keyframe : undefined,
          data: Buffer.from(value.data).toString("base64"),
        }));
      }
    })();

    socket.on("message", async raw => {
      if (!client.controller) return;
      try {
        const message = JSON.parse(raw.toString()) as
          | { type: "touch"; action: "down" | "move" | "up"; x: number; y: number }
          | { type: "rotate" };
        if (message.type === "rotate") {
          await client.controller.rotateDevice();
        } else if (message.type === "touch") {
          await client.controller.injectTouch({
            action: message.action === "down"
              ? AndroidMotionEventAction.Down
              : message.action === "up"
                ? AndroidMotionEventAction.Up
                : AndroidMotionEventAction.Move,
            pointerId: ScrcpyPointerId.Mouse,
            pointerX: Math.max(0, message.x),
            pointerY: Math.max(0, message.y),
            videoWidth: video.width,
            videoHeight: video.height,
            pressure: message.action === "up" ? 0 : 0.5,
            actionButton: AndroidMotionEventButton.Primary,
            buttons: AndroidMotionEventButton.Primary,
          });
        }
      } catch (error) {
        socket.send(JSON.stringify({
          type: "error",
          error: error instanceof Error ? error.message : "Invalid control message.",
        }));
      }
    });

    socket.on("close", () => {
      void reader.cancel();
      void client.close();
      void adb.close();
    });

    void videoTask.catch(error => {
      socket.send(JSON.stringify({
        type: "error",
        error: error instanceof Error ? error.message : "Video stream failed.",
      }));
    });
  } catch (error) {
    socket.send(JSON.stringify({
      type: "error",
      error: error instanceof Error ? error.message : "Scrcpy could not start.",
    }));
    socket.close();
  }
});

server.listen(port, "127.0.0.1", () => {
  console.log(`Droidwart admin listening at http://127.0.0.1:${port}`);
  console.log(`ADB server: ${adbHost}:${adbPort}`);
});
