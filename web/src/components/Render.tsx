import { motion } from 'motion/react'
import type { Device } from '../bridge'
import { useMemo } from 'react'
import { layoutFor, maskFor, renderFor } from '../devices'
import { LightGlow } from './LightGlow'
import { KindIcon } from './ui'

/**
 * The product render on its "stage": a soft spotlight behind, an elliptical floor shadow
 * below, no box around it. Falls back to a large line icon when there is no render.
 */
export function Render({ device, size, dim, lift }: { device: Device; size: number; dim?: boolean; lift?: boolean }) {
  const src = renderFor(device)
  return (
    <div className="relative grid place-items-center" style={{ width: size, height: size }}>
      <div
        className="absolute inset-[-10%] rounded-full"
        style={{ background: 'radial-gradient(closest-side, rgb(255 255 255 / .055), transparent 72%)' }}
      />
      <div
        className="absolute bottom-[6%] h-[9%] w-[62%] rounded-[50%] blur-[14px]"
        style={{ background: 'rgb(0 0 0 / .75)' }}
      />
      <motion.div
        className="relative grid size-full place-items-center"
        animate={{ y: lift ? -6 : 0, scale: lift ? 1.02 : 1 }}
        transition={{ duration: 0.32, ease: [0.2, 0, 0, 1] }}
        style={{ filter: dim ? 'grayscale(1) brightness(.55)' : undefined, opacity: dim ? 0.6 : 1 }}
      >
        {src ? (
          <>
          <img
            src={src}
            alt=""
            draggable={false}
            className="absolute left-[10%] top-[8%] h-[80%] w-[80%] object-contain"
            style={{ filter: 'drop-shadow(0 0 1px rgb(255 255 255 / .18)) drop-shadow(0 18px 24px rgb(0 0 0 / .55))' }}
          />
          {!dim && <Glow device={device} size={size} />}
          </>
        ) : (
          <KindIcon kind={device.kind} size={size * 0.38} className="text-fg-faint" />
        )}
      </motion.div>
    </div>
  )
}

/**
 * The mouse's lighting colors on the render, drawn with Glorious CORE 2's preview code (see
 * LightGlow) as a still picture, shown only through the LED areas of the mask, with a soft bloom.
 */
function Glow({ device, size }: { device: Device; size: number }) {
  const mask = maskFor(device)
  const layout = layoutFor(device)
  const frame = useMemo(() => (layout ? { width: layout.width, height: layout.height, bbox: layout.bbox as [number, number, number, number] } : null), [layout])
  const c = device.config
  if (!mask || !c || !frame || c.effect === 0 || c.features.effects) return null
  // Place the canvas exactly over the picture (object-contain inside an 80% box).
  const box = size * 0.8
  const ratio = (frame.bbox[2] - frame.bbox[0]) / (frame.bbox[3] - frame.bbox[1])
  const w = ratio < 1 ? box * ratio : box
  const h = ratio < 1 ? box : box / ratio
  const pos = { left: size * 0.1 + (box - w) / 2, top: size * 0.08 + (box - h) / 2, width: w, height: h }
  const glow = { mask, effect: c.effect, speed: c.effectSpeed || 60, brightness: device.brightness ?? 100, color: c.effectColor || '#ff0000', frame }
  return (
    <>
      <div className="pointer-events-none absolute opacity-80 blur-[6px]" style={pos}><LightGlow {...glow} /></div>
      <div className="pointer-events-none absolute" style={pos}><LightGlow {...glow} /></div>
    </>
  )
}
