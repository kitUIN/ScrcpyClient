import React, { useEffect, useRef, useState } from "https://esm.sh/react@18.3.1";
import { createRoot } from "https://esm.sh/react-dom@18.3.1/client";
import htm from "https://esm.sh/htm@3.1.1";

const html = htm.bind(React.createElement);
const FRAME_HEADER_BYTES = 12;

function getStatusValue(status, camelName, pascalName) {
  if (status && Object.prototype.hasOwnProperty.call(status, camelName)) {
    return status[camelName];
  }

  return status ? status[pascalName] : undefined;
}

function normalizeStatus(status) {
  return {
    mode: getStatusValue(status, "mode", "Mode") ?? "loading",
    url: getStatusValue(status, "url", "Url") ?? "",
    previewFps: getStatusValue(status, "previewFps", "PreviewFps") ?? 30,
    connected: getStatusValue(status, "connected", "Connected") ?? false,
    deviceSerial: getStatusValue(status, "deviceSerial", "DeviceSerial") ?? null,
    startedAtUtc: getStatusValue(status, "startedAtUtc", "StartedAtUtc") ?? null,
    startupError: getStatusValue(status, "startupError", "StartupError") ?? "",
    hasFrame: getStatusValue(status, "hasFrame", "HasFrame") ?? false,
    frameWidth: getStatusValue(status, "frameWidth", "FrameWidth") ?? null,
    frameHeight: getStatusValue(status, "frameHeight", "FrameHeight") ?? null,
    frameNumber: getStatusValue(status, "frameNumber", "FrameNumber") ?? null,
    presentationTimestampUs: getStatusValue(status, "presentationTimestampUs", "PresentationTimestampUs") ?? null,
    lastFrameAtUtc: getStatusValue(status, "lastFrameAtUtc", "LastFrameAtUtc") ?? null
  };
}

function formatTimestamp(value) {
  if (!value) {
    return "waiting";
  }

  return new Date(value).toLocaleTimeString();
}

function formatFrameSize(status) {
  if (!status.frameWidth || !status.frameHeight) {
    return "waiting";
  }

  return `${status.frameWidth} × ${status.frameHeight}`;
}

function App() {
  const [status, setStatus] = useState({
    mode: "loading",
    connected: false,
    hasFrame: false,
    previewFps: 30
  });
  const [frameState, setFrameState] = useState("loading");
  const [errorMessage, setErrorMessage] = useState("");
  const canvasRef = useRef(null);
  const reconnectTimerRef = useRef(0);
  const renderSequenceRef = useRef(0);
  const lastRenderedFrameNumberRef = useRef(-1);
  const hasRenderedFrameRef = useRef(false);

  useEffect(() => {
    let active = true;
    let socket = null;

    function renderFrame(frameData) {
      const renderSequence = ++renderSequenceRef.current;
      const buffer = frameData instanceof ArrayBuffer ? frameData : frameData.buffer;
      if (buffer.byteLength < FRAME_HEADER_BYTES) {
        throw new Error("Frame packet is too small.");
      }

      const header = new DataView(buffer, 0, FRAME_HEADER_BYTES);
      const width = header.getInt32(0, true);
      const height = header.getInt32(4, true);
      const frameNumber = header.getInt32(8, true);

      if (width <= 0 || height <= 0) {
        throw new Error("Invalid frame dimensions.");
      }

      if (frameNumber <= lastRenderedFrameNumberRef.current) {
        return;
      }

      const expectedLength = FRAME_HEADER_BYTES + (width * height * 4);
      if (buffer.byteLength !== expectedLength) {
        throw new Error("Unexpected frame payload size.");
      }

      if (!active || renderSequence !== renderSequenceRef.current) {
        return;
      }

      const canvas = canvasRef.current;
      if (!canvas) {
        return;
      }

      const context = canvas.getContext("2d", { alpha: false });
      if (!context) {
        throw new Error("Canvas 2D context is unavailable.");
      }

      if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
      }

      const pixels = new Uint8ClampedArray(buffer, FRAME_HEADER_BYTES, width * height * 4);
      context.putImageData(new ImageData(pixels, width, height), 0, 0);
      lastRenderedFrameNumberRef.current = frameNumber;
      hasRenderedFrameRef.current = true;
      setFrameState("ready");
    }

    function connect() {
      const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
      socket = new WebSocket(`${protocol}//${window.location.host}/ws`);
      socket.binaryType = "arraybuffer";

      socket.onopen = () => {
        if (!active) {
          return;
        }

        setErrorMessage("");
        setFrameState(current => (current === "ready" || hasRenderedFrameRef.current ? current : "waiting"));
      };

      socket.onmessage = event => {
        if (!active) {
          return;
        }

        if (typeof event.data === "string") {
          const message = JSON.parse(event.data);
          if (message.type === "status") {
            const nextStatus = normalizeStatus(message.payload);
            setStatus(nextStatus);
            setErrorMessage(nextStatus.startupError || "");
            if (!nextStatus.hasFrame && !hasRenderedFrameRef.current) {
              setFrameState("waiting");
            }
          }

          return;
        }

        try {
          renderFrame(event.data);
        } catch (error) {
          if (!active) {
            return;
          }

          setFrameState("error");
          setErrorMessage(error.message || "Failed to decode frame.");
        }
      };

      socket.onerror = () => {
        if (!active) {
          return;
        }

        setFrameState("error");
        setErrorMessage("WebSocket connection failed.");
      };

      socket.onclose = () => {
        if (!active) {
          return;
        }

        setFrameState(current => (current === "ready" ? "waiting" : "error"));
        setErrorMessage(current => current || "WebSocket disconnected, retrying...");
        reconnectTimerRef.current = window.setTimeout(connect, 1000);
      };
    }

    connect();

    return () => {
      active = false;
      if (reconnectTimerRef.current) {
        window.clearTimeout(reconnectTimerRef.current);
      }

      if (socket) {
        socket.close();
      }

      lastRenderedFrameNumberRef.current = -1;
      hasRenderedFrameRef.current = false;
    };
  }, []);

  const statusTone = errorMessage ? "bad" : status.connected ? "good" : "warn";

  return html`
    <main className="shell">
      <section className="hero">
        <div>
          <p className="eyebrow">Scrcpy React Frontend</p>
          <h1>浏览器里看设备画面</h1>
          <p className="lede">
            后端继续复用现有 scrcpy 解码链路，前端通过 WebSocket 持续接收状态和最新帧，避免轮询带来的额外请求开销。
          </p>
        </div>
        <div className="chips">
          <span className=${`chip chip--${statusTone}`}>${errorMessage ? "startup error" : status.connected ? "stream live" : "waiting for stream"}</span>
          <span className="chip">mode ${status.mode}</span>
          <span className="chip">preview ${status.previewFps || 30} fps</span>
        </div>
      </section>

      <section className="stage-card">
        <div className="stage-meta">
          <div>
            <span className="meta-label">device</span>
            <strong>${status.deviceSerial || "not attached"}</strong>
          </div>
          <div>
            <span className="meta-label">resolution</span>
            <strong>${formatFrameSize(status)}</strong>
          </div>
          <div>
            <span className="meta-label">last frame</span>
            <strong>${formatTimestamp(status.lastFrameAtUtc)}</strong>
          </div>
          <div>
            <span className="meta-label">frame no.</span>
            <strong>${status.frameNumber ?? "waiting"}</strong>
          </div>
        </div>

        <div className=${`screen screen--${frameState}`}>
          <canvas className="screen-image" ref=${canvasRef}></canvas>
          ${frameState === "ready"
            ? null
            : html`<div className="screen-placeholder">
                <div className="pulse"></div>
                <p>${errorMessage || "等待首帧到达"}</p>
              </div>`}
        </div>
      </section>

      <section className="info-grid">
        <article className="info-card">
          <p className="meta-label">pipeline</p>
          <h2>解码链路不变</h2>
          <p>继续使用现有 FFmpeg 解码和 LatestFrameSink，只把展示层换成浏览器页面。</p>
        </article>
        <article className="info-card">
          <p className="meta-label">transport</p>
          <h2>Raw frame stream</h2>
          <p>页面通过 WebSocket 接收原始 RGBA 帧包，直接写入 ImageData 后绘制到 canvas，避免图片编解码的额外开销。</p>
        </article>
        <article className="info-card">
          <p className="meta-label">processor</p>
          <h2>兼容现有处理器</h2>
          <p>如果启用 farm-test，浏览器里看到的是处理后的叠加结果，不需要额外前端算法。</p>
        </article>
      </section>
    </main>
  `;
}

createRoot(document.getElementById("root")).render(html`<${App} />`);