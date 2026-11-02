# Synthetic instructional-media fixtures

These are generated test patterns, not recordings of people or completed training lessons.
`synthetic-guide.mp4`: 2 seconds, 320×180, 24 fps, H.264/yuv420p, no audio, fast-start MP4.
`synthetic-thumbnail.png`: 320×180 solid burgundy PNG.

Generated locally with FFmpeg's `testsrc2` and `color` sources. FFmpeg is **not** a production runtime dependency. Used by unit, API, Browser and iOS QA only. No child images, external services, financial operations or customer data.
