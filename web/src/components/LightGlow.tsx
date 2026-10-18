import { useEffect, useRef } from 'react'

/**
 * The mouse's lighting colors drawn on its render, using Glorious CORE 2's LightingPreview drawing code
 * (renderer bundle: updateGloriousMode, updateBreathingMode, updateSeamlessBreathingMode,
 * updateNormallyOnMode, updateTailMode, updateRaveMode, updateWaveMode). CORE advances its animation
 * one step per animation frame; we run the same steps at a fixed 60 per second so a fast monitor
 * doesn't speed it up. Drawing happens in CORE's own canvas coordinates (the device layout frame,
 * e.g. 750 x 372) and is mapped onto our cropped render, then masked to the LED areas.
 */

type RGB = { r: number; g: number; b: number }
const rgb = (r: number, g: number, b: number): RGB => ({ r, g, b })
const hex = (c: RGB) => `#${[c.r, c.g, c.b].map((v) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')).join('')}`
const fromHex = (h: string): RGB => {
  const n = parseInt(h.replace('#', ''), 16) || 0
  return rgb((n >> 16) & 255, (n >> 8) & 255, n & 255)
}
const lerp = (a: RGB, b: RGB, t: number) => rgb(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t)

const GLORIOUS = [rgb(255, 0, 0), rgb(255, 154, 0), rgb(208, 222, 33), rgb(79, 220, 74), rgb(63, 218, 216), rgb(47, 201, 226), rgb(28, 127, 238), rgb(95, 21, 242), rgb(186, 12, 248), rgb(251, 7, 217)]
const TAIL = GLORIOUS
const WAVE = [rgb(251, 7, 217), rgb(79, 220, 74), rgb(28, 127, 238), rgb(255, 0, 0), rgb(255, 255, 255), rgb(63, 218, 216), rgb(208, 222, 33)]

export interface GlowFrame {
  /** CORE's canvas size (device layout units) and the part of it our render shows. */
  width: number
  height: number
  bbox: [number, number, number, number]
}

/** Firmware effect numbers of the Model O / PixArt families. */
const enum Fx { Off = 0, Glorious = 1, Seamless = 2, Breathing = 3, Static = 4, BreathingSingle = 5, Tail = 6, Rave = 7, Wave = 8 }

export function LightGlow({ mask, effect, speed, brightness, color, frame }: { mask: string; effect: number; speed: number; brightness: number; color: string; frame: GlowFrame }) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const props = useRef({ effect, speed, brightness, color })
  const redraw = useRef<() => void>(() => {})
  props.current = { effect, speed, brightness, color }

  useEffect(() => {
    const el = canvas.current
    if (!el) return
    const ctx = el.getContext('2d')!
    const { width: W, height: H, bbox } = frame
    const [bx0, by0, bx1, by1] = bbox

    // Animation state, as in CORE.
    let gloriousColors = [...GLORIOUS]
    let gloriousAcc = 0
    let gloriousRot = GLORIOUS.length - 1
    const breathing = { op: 'decrement', light: 100, target: 0 }
    const seamless = { target: 0, mix: 1, percent: 0 }
    const tail = { colorIndex: 0, colorOffset: 0, rotation: 4, display: 10, elapsed: 0 }
    const rave = { colorIndex: 0, direction: 0, display: 10, elapsed: 0 }
    const waveStops = WAVE.map((c, i) => ({ hex: hex(c), stop: (100 / WAVE.length) * i }))
    let waveScroll = 0
    let waveAcc = 0
    let lastEffect = -1

    const tick = () => {
      const { effect, speed, brightness } = props.current
      if (effect !== lastEffect) {
        lastEffect = effect
        gloriousColors = [...GLORIOUS]
      }
      switch (effect) {
        case Fx.Glorious: {
          const step = speed * 5e-4
          gloriousAcc += step
          if (gloriousAcc > 1) {
            gloriousAcc = 0
            gloriousRot--
            if (gloriousRot < 0) gloriousRot = GLORIOUS.length - 1
          }
          for (let i = 0; i < gloriousColors.length - 1; i++) {
            let other = i + gloriousRot
            if (other > GLORIOUS.length - 1) other -= GLORIOUS.length
            gloriousColors[i] = lerp(gloriousColors[i], GLORIOUS[other], step)
          }
          break
        }
        case Fx.Breathing:
        case Fx.BreathingSingle: {
          const targets = effect === Fx.Breathing ? GLORIOUS : [fromHex(props.current.color)]
          if (breathing.light <= 0) {
            breathing.light = 0
            breathing.op = 'increment'
            breathing.target = (breathing.target + 1) % targets.length
          } else if (breathing.light >= brightness) {
            breathing.light = brightness
            breathing.op = 'decrement'
          }
          const step = (speed / 90) / (100 / Math.max(1, brightness))
          breathing.light += breathing.op === 'decrement' ? -step : step
          if (breathing.target > targets.length - 1) breathing.target = 0
          break
        }
        case Fx.Seamless: {
          if (seamless.percent >= 100) {
            seamless.percent = 0
            seamless.target += 1
            seamless.mix = seamless.target + 1
          }
          if (seamless.target > GLORIOUS.length - 1) seamless.target = 0
          if (seamless.mix > GLORIOUS.length - 1) seamless.mix = 0
          seamless.percent += speed / 90
          break
        }
        case Fx.Tail: {
          if (tail.elapsed >= tail.display) {
            tail.rotation++
            if (tail.rotation > TAIL.length - 1) {
              tail.rotation = 0
              tail.colorOffset = (tail.colorOffset + 1) % TAIL.length
            }
            tail.colorIndex = (tail.colorIndex + 1) % TAIL.length
            tail.elapsed = 0
          }
          tail.elapsed++
          break
        }
        case Fx.Rave: {
          // CORE's preview holds each step for a fixed 10 frames (~167 ms), which matches the mouse only
          // at speed 90 (firmware delay 30). The firmware waits ~5.5 ms per delay unit, so follow the real
          // delay: (105 - speed) * 2 units -> frames.
          rave.display = Math.max(2, Math.round(((105 - speed) * 2 * 5.56) / (1000 / 60)))
          if (rave.elapsed >= rave.display) {
            if (rave.direction === 0) rave.direction = 1
            else {
              rave.direction = 0
              rave.colorIndex = rave.colorIndex === 0 ? 1 : 0
            }
            rave.elapsed = 0
          }
          rave.elapsed++
          break
        }
        case Fx.Wave: {
          waveAcc += speed / 90
          if (waveAcc > 20) {
            waveScroll += 20
            waveAcc = 0
          }
          if (waveScroll > 100) waveScroll = 0
          break
        }
      }
    }

    const draw = () => {
      const { effect, brightness, color } = props.current
      // Map CORE's canvas (W x H) onto our crop of the render.
      const sx = el.width / (bx1 - bx0)
      const sy = el.height / (by1 - by0)
      ctx.setTransform(1, 0, 0, 1, 0, 0)
      ctx.clearRect(0, 0, el.width, el.height)
      ctx.setTransform(sx, 0, 0, sy, -bx0 * sx, -by0 * sy)
      const vertical = () => ctx.createLinearGradient(W / 2, 0, W / 2, H)
      let fill: string | CanvasGradient | null = null
      let alpha = 1
      switch (effect) {
        case Fx.Glorious: {
          const radius = W + 30
          const x = W / 2 + 20
          const y = H / 3 - 30
          const g = ctx.createRadialGradient(x, y, 0, x - 10, y + 5, radius)
          gloriousColors.forEach((c, i) => g.addColorStop(Math.max(0, Math.min(1, i / (gloriousColors.length - 1) - i * 0.05)), hex(c)))
          fill = g
          break
        }
        case Fx.Breathing:
        case Fx.BreathingSingle: {
          const targets = effect === Fx.Breathing ? GLORIOUS : [fromHex(color)]
          const t = targets[Math.min(breathing.target, targets.length - 1)]
          const k = Math.max(0, breathing.light) / 100
          fill = hex(rgb(t.r * k, t.g * k, t.b * k))
          break
        }
        case Fx.Seamless: {
          const k = seamless.percent / 100
          fill = hex(lerp(GLORIOUS[seamless.target], GLORIOUS[seamless.mix], k))
          break
        }
        case Fx.Static:
          fill = color
          alpha = brightness / 100 // CORE: rgb(color / brightness)
          break
        case Fx.Tail: {
          const colors = TAIL.slice(tail.colorOffset).concat(TAIL.slice(0, tail.colorOffset))
          const section = H / colors.length
          let ci = tail.colorIndex + tail.colorOffset
          while (ci > colors.length - 1) ci -= colors.length
          const top = (tail.rotation * section) / H
          const bottom = (tail.rotation * section + section) / H
          const g = vertical()
          if (top > 0) {
            g.addColorStop(0, '#00000000')
            g.addColorStop(Math.max(top - 1e-4, 0), '#00000000')
          }
          g.addColorStop(top, hex(colors[ci]))
          g.addColorStop(Math.min(bottom, 1), hex(colors[ci]))
          g.addColorStop(Math.min(bottom + 1e-4, 1), '#00000000')
          g.addColorStop(1, '#00000000')
          fill = g
          break
        }
        case Fx.Rave: {
          const c = rave.colorIndex === 0 ? '#FF0000' : '#0000FF'
          const g = vertical()
          if (rave.direction === 0) {
            g.addColorStop(0, c)
            g.addColorStop(1, '#000000')
          } else {
            g.addColorStop(0, '#000000')
            g.addColorStop(1, c)
          }
          fill = g
          break
        }
        case Fx.Wave: {
          const g = vertical()
          for (const s of waveStops) {
            let v = s.stop - waveScroll
            if (v < 0) v = 100 + v
            g.addColorStop(Math.max(0, Math.min(1, v / 100)), s.hex)
          }
          fill = g
          break
        }
      }
      if (fill) {
        ctx.globalAlpha = Math.max(0, Math.min(1, alpha))
        ctx.fillStyle = fill
        ctx.fillRect(0, 0, W, H)
        ctx.globalAlpha = 1
      }
    }

    // The animation itself is not shown: no public source (CORE, OpenRGB) knows how fast the firmware
    // actually plays each effect, so a moving preview would show the wrong speed. We draw CORE's first
    // frame of the effect as a still picture of its colors, and redraw only when the settings change.
    void tick
    let raf = requestAnimationFrame(draw)
    redraw.current = () => {
      cancelAnimationFrame(raf)
      raf = requestAnimationFrame(draw)
    }
    return () => cancelAnimationFrame(raf)
  }, [frame])

  useEffect(() => redraw.current(), [effect, brightness, color])

  const maskStyle = {
    WebkitMaskImage: `url(${mask})`,
    maskImage: `url(${mask})`,
    WebkitMaskSize: '100% 100%',
    maskSize: '100% 100%',
    WebkitMaskRepeat: 'no-repeat',
    maskRepeat: 'no-repeat',
  } as const
  return <canvas ref={canvas} width={240} height={Math.round((240 * (frame.bbox[3] - frame.bbox[1])) / (frame.bbox[2] - frame.bbox[0]))} className="size-full" style={{ ...maskStyle, mixBlendMode: 'screen' }} />
}
