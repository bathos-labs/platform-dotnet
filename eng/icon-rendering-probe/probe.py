"""Headless dependency experiment; not a shipping decoder or .NET integration test."""
import sys
import tempfile
from pathlib import Path
import cairo
import gi

gi.require_version('GdkPixbuf', '2.0')
gi.require_version('Rsvg', '2.0')
from gi.repository import GdkPixbuf, GLib, Rsvg


def decode(data, kind):
    loader = GdkPixbuf.PixbufLoader.new_with_type(kind)
    loader.connect('size-prepared', lambda obj, w, h: obj.set_size(32, 16))
    loader.write(data)
    loader.close()
    pixbuf = loader.get_pixbuf()
    assert (pixbuf.get_width(), pixbuf.get_height()) == (32, 16)
    assert pixbuf.get_bits_per_sample() == 8
    return bytes(pixbuf.get_pixels())


def svg(body):
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="64" height="32">'
            + body + '</svg>').encode()


def render(data):
    handle = Rsvg.Handle.new_from_data(data)  # Deliberately no base URI.
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, 32, 32)
    context = cairo.Context(surface)
    viewport = Rsvg.Rectangle()
    viewport.x, viewport.y, viewport.width, viewport.height = 0, 8, 32, 16
    assert handle.render_document(context, viewport)
    surface.flush()
    return bytes(surface.get_data())


formats = sorted(f.get_name() for f in GdkPixbuf.Pixbuf.get_formats())
print('GdkPixbuf formats:', ', '.join(formats))
with tempfile.TemporaryDirectory() as directory:
    path = Path(directory) / 'red.png'
    image = cairo.ImageSurface(cairo.FORMAT_ARGB32, 64, 32)
    ctx = cairo.Context(image)
    ctx.set_source_rgba(1, 0, 0, 0.5)
    ctx.paint()
    image.write_to_png(str(path))
    png = decode(path.read_bytes(), 'png')
    assert png[:4] == bytes([255, 0, 0, 128]), png[:4]
    xpm = b'/* XPM */\nstatic char *x[]={"2 1 2 1","r c #ff0000",". c None","r."};'
    assert decode(xpm, 'xpm')
    data = svg('<rect width="64" height="32" fill="red" fill-opacity="0.5"/>')
    pixels = render(data)
    assert pixels[:32 * 8 * 4] == bytes(32 * 8 * 4)
    # Cairo native ARGB32 is BGRA on these little-endian test hosts.
    assert sys.byteorder == 'little'
    assert pixels[32 * 8 * 4:32 * 8 * 4 + 4] == bytes([0, 0, 128, 128])
    blank = render(svg(''))
    for reference in [path.as_uri(), 'red.png', 'https://example.invalid/icon.png']:
        assert render(svg(f'<image href="{reference}" width="64" height="32"/>')) == blank
    for action in [lambda: decode(b'not a png', 'png'), lambda: render(b'<svg broken')]:
        try:
            action()
        except GLib.Error:
            pass
        else:
            raise AssertionError('Malformed input accepted')
    if 'svg' in formats:
        assert decode(data, 'svg')
        print('SVG plugin: rendering passed')
    else:
        try:
            decode(data, 'svg')
        except GLib.Error:
            print('SVG plugin: unavailable, explicit failure confirmed')
        else:
            raise AssertionError('Unexpected SVG plugin')
print('PASS: PNG/XPM decode, direct SVG, alpha, padding, external-reference denial, malformed input')
