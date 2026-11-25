import { useEffect, useRef, useState } from 'react'
import { BitmapVideoFrameRenderer, WebCodecsVideoDecoder } from '@yume-chan/scrcpy-decoder-webcodecs'
import { ScrcpyVideoCodecId } from '@yume-chan/scrcpy'
import './App.css'

interface Device {
  serial: string
  state: 'unauthorized' | 'offline' | 'device'
  model?: string
  transportId: string
}

function App() {
  const [devices, setDevices] = useState<Device[]>([])
  const [selected, setSelected] = useState('')
  const [packageName, setPackageName] = useState('')
  const [apk, setApk] = useState<File | null>(null)
  const [message, setMessage] = useState('')
  const [setting, setSetting] = useState('screen_off_timeout')
  const [settingValue, setSettingValue] = useState('600000')
  const screenRef = useRef<HTMLCanvasElement>(null)
  const decoderRef = useRef<WebCodecsVideoDecoder | null>(null)
  const writerRef = useRef<WritableStreamDefaultWriter<unknown> | null>(null)

  const refresh = async () => {
    const response = await fetch('/api/devices')
    setDevices(await response.json())
  }

  useEffect(() => {
    void refresh()
  }, [])

  const post = async (path: string, body?: BodyInit) => {
    const response = await fetch(path, {
      method: 'POST',
      body,
      headers: body instanceof FormData ? undefined : { 'Content-Type': 'application/json' },
    })
    const result = await response.json()
    if (!response.ok) throw new Error(result.error ?? 'Request failed')
    setMessage(JSON.stringify(result, null, 2))
  }

  const install = async () => {
    if (!selected || !apk) return
    const form = new FormData()
    form.append('apk', apk)
    form.append('replace', 'true')
    form.append('grant', 'true')
    await post(`/api/devices/${encodeURIComponent(selected)}/apps/install`, form)
  }

  const startScrcpy = () => {
    if (!selected) return
    const canvas = screenRef.current
    if (!canvas) return
    if (!WebCodecsVideoDecoder.isSupported) {
      setMessage('WebCodecs is not supported by this browser.')
      return
    }

    decoderRef.current?.dispose()
    const renderer = new BitmapVideoFrameRenderer(canvas)
    const decoder = new WebCodecsVideoDecoder({
      codec: ScrcpyVideoCodecId.H264,
      renderer,
    })
    decoderRef.current = decoder
    const writer = decoder.writable.getWriter()
    writerRef.current = writer as WritableStreamDefaultWriter<unknown>

    const socket = new WebSocket(
      `${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/ws/scrcpy/${encodeURIComponent(selected)}`
    )
    socket.onopen = () => setMessage('Scrcpy session connected')
    socket.onmessage = event => {
      if (typeof event.data !== 'string') return
      const packet = JSON.parse(event.data) as {
        type: 'ready' | 'video' | 'error'
        packetType?: 'configuration' | 'data'
        keyframe?: boolean
        data?: string
        width?: number
        height?: number
        error?: string
      }
      if (packet.type === 'error') {
        setMessage(packet.error ?? 'Scrcpy error')
      } else if (packet.type === 'ready') {
        setMessage(`Scrcpy ${packet.width}x${packet.height}`)
      } else if (packet.type === 'video' && packet.data) {
        const data = Uint8Array.from(atob(packet.data), character => character.charCodeAt(0))
        const mediaPacket = packet.packetType === 'configuration'
          ? { type: 'configuration' as const, data }
          : { type: 'data' as const, keyframe: packet.keyframe, data }
        void (writer as WritableStreamDefaultWriter<typeof mediaPacket>).write(mediaPacket)
      }
    }
    socket.onclose = () => setMessage('Scrcpy session closed')

    canvas.onpointerdown = event => {
      const rect = canvas.getBoundingClientRect()
      socket.send(JSON.stringify({
        type: 'touch',
        action: 'down',
        x: (event.clientX - rect.left) * canvas.width / rect.width,
        y: (event.clientY - rect.top) * canvas.height / rect.height,
      }))
    }
    canvas.onpointermove = event => {
      if (!event.buttons) return
      const rect = canvas.getBoundingClientRect()
      socket.send(JSON.stringify({
        type: 'touch',
        action: 'move',
        x: (event.clientX - rect.left) * canvas.width / rect.width,
        y: (event.clientY - rect.top) * canvas.height / rect.height,
      }))
    }
    canvas.onpointerup = event => {
      const rect = canvas.getBoundingClientRect()
      socket.send(JSON.stringify({
        type: 'touch',
        action: 'up',
        x: (event.clientX - rect.left) * canvas.width / rect.width,
        y: (event.clientY - rect.top) * canvas.height / rect.height,
      }))
    }
  }

  return (
    <main className="admin">
      <header>
        <h1>Droidwart Administration</h1>
        <button type="button" onClick={() => void refresh()}>Refresh</button>
      </header>

      <section>
        <h2>Devices</h2>
        <select value={selected} onChange={event => setSelected(event.target.value)}>
          <option value="">Select device</option>
          {devices.map(device => (
            <option key={device.transportId} value={device.serial}>
              {device.model || device.serial} ({device.serial}) — {device.state}
            </option>
          ))}
        </select>
      </section>

      <section className="grid">
        <article>
          <h2>Apps</h2>
          <input value={packageName} onChange={event => setPackageName(event.target.value)} placeholder="com.example.app" />
          <div className="actions">
            <button type="button" onClick={() => void post(`/api/devices/${encodeURIComponent(selected)}/apps/${encodeURIComponent(packageName)}/start`).catch(error => setMessage(error.message))}>Start</button>
            <button type="button" onClick={() => void post(`/api/devices/${encodeURIComponent(selected)}/apps/${encodeURIComponent(packageName)}/stop`).catch(error => setMessage(error.message))}>Stop</button>
            <button type="button" onClick={() => void post(`/api/devices/${encodeURIComponent(selected)}/apps/${encodeURIComponent(packageName)}/restart`).catch(error => setMessage(error.message))}>Restart</button>
          </div>
          <input type="file" accept=".apk,application/vnd.android.package-archive" onChange={event => setApk(event.target.files?.[0] ?? null)} />
          <button type="button" onClick={() => void install().catch(error => setMessage(error.message))}>Install APK</button>
        </article>

        <article>
          <h2>Settings</h2>
          <select value={setting} onChange={event => setSetting(event.target.value)}>
            <option value="stay_on_while_plugged">Stay on while plugged</option>
            <option value="screen_off_timeout">Screen off timeout</option>
            <option value="bluetooth_on">Bluetooth</option>
            <option value="wifi_on">Wi-Fi</option>
          </select>
          <input value={settingValue} onChange={event => setSettingValue(event.target.value)} />
          <button type="button" onClick={() => {
            void post(`/api/devices/${encodeURIComponent(selected)}/settings/${setting}`, JSON.stringify({ value: settingValue })).catch(error => setMessage(error.message))
          }}>Apply setting</button>
        </article>

        <article>
          <h2>Remote view</h2>
          <canvas ref={screenRef} width="720" height="1280" />
          <button type="button" onClick={startScrcpy}>Start Scrcpy</button>
        </article>
      </section>

      <pre>{message}</pre>
    </main>
  )
}

export default App
