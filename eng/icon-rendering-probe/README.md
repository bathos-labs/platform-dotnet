# Linux icon rendering experiment

This headless dependency probe evaluates options for replacing encoded Linux icon results with a
uniform owned RGBA bitmap. It is not shipping code or .NET interop acceptance. Python and GI are
probe tooling only. The shipping implementation now follows the direct-rendering approach tested
here.

```sh
docker build -t bathos-icon-probe eng/icon-rendering-probe
docker run --rm --network none bathos-icon-probe
docker run --rm bathos-icon-probe sh -c 'apt-get update -qq && apt-get install -y --no-install-recommends librsvg2-common && python3 /probe.py'
```

## Observed results

On November 8, 2026, Ubuntu 24.04 Arm64 in Docker passed both configurations with GdkPixbuf 2.42.10,
librsvg 2.58.0, and Cairo 1.18.0:

- GdkPixbuf decoded PNG and XPM at the requested dimensions. The PNG assertion verified straight
  alpha and channel values.
- Direct librsvg/Cairo rendered SVG without a display server or GdkPixbuf SVG plugin. Assertions
  verified premultiplied alpha, channel order, and transparent padding on the little-endian host.
- Loading SVG bytes without a base URI omitted tested relative-file, absolute-file, and HTTPS image
  references. This is a rendering assertion, not network instrumentation or a complete security
  audit.
- Malformed PNG and SVG inputs raised errors.
- Without `librsvg2-common`, the GdkPixbuf SVG loader was absent and explicitly failed. Installing
  that package enabled SVG decoding through GdkPixbuf.

## Proposed direction

Use GdkPixbuf for PNG/XPM and direct librsvg/Cairo for SVG, scoped to the Linux implementation.
Direct SVG rendering avoids dependence on loader registration and permits an explicit no-base-URI
policy. The trade-off is additional native interop and conversion from Cairo's native-endian,
premultiplied pixels to the core's straight-alpha RGBA contract. Both paths can write or copy into
one final owned managed buffer; no PNG re-encoding is needed.

The
[librsvg reference policy](https://gnome.pages.gitlab.gnome.org/librsvg/Rsvg-2.0/class.Handle.html)
permits embedded data URLs even without a base URI. External resources omitted by the renderer do
not necessarily cause an error. A bounded output bitmap and bounded input bytes do not establish
bounded decoder memory or CPU usage. Keep default parser limits and investigate source-dimension,
embedded-image, cancellation, and resource-limit behavior before integrating the provider.

Remaining acceptance includes .NET P/Invoke signatures and cleanup, RGB/RGBA row-stride handling,
color-profile normalization, square fitting, missing native libraries, concurrent calls, ownership
transfer, and the Linux x64 CI target. These experiments establish feasibility, not those
guarantees. The proposed public result remains renderer-independent. No shared rendering layer is
justified by the existing Windows/macOS implementations.
