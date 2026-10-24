"""Headless protocol fixture on its own session bus. This never invokes a desktop picker."""
import os
import sys

import dbus
import dbus.lowlevel
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib

DBusGMainLoop(set_as_default=True)
bus = dbus.SessionBus(private=True)
portal = "org.freedesktop.portal.Desktop"
assert bus.request_name(portal, dbus.bus.NAME_FLAG_DO_NOT_QUEUE) == dbus.bus.REQUEST_NAME_REPLY_PRIMARY_OWNER
loop = GLib.MainLoop()
scenario = sys.argv[1]
client = None
bus.add_match_string("type='signal',sender='org.freedesktop.DBus',interface='org.freedesktop.DBus',member='NameOwnerChanged'")


def handle(connection, call):
    global client
    if call.get_interface() == "org.freedesktop.DBus" and call.get_member() == "NameOwnerChanged":
        name, previous, current = call.get_args_list()
        if name == client and not current:
            loop.quit()
        return
    if call.get_interface() != "org.freedesktop.portal.Screenshot" or call.get_member() != "PickColor":
        return
    parent, options = call.get_args_list()
    client = call.get_sender()
    assert parent == ""
    assert str(options["handle_token"]).startswith("bathos_")
    if scenario == "method-error":
        connection.send_message(dbus.lowlevel.ErrorMessage(call, "org.freedesktop.DBus.Error.NotSupported", "No color picker backend"))
        connection.flush()
        return True
    # Deliberately use a legacy path and emit Response BEFORE the method reply to cover both lifecycle races.
    path = "/org/freedesktop/portal/desktop/request/legacy/" + str(options["handle_token"])
    reply = dbus.lowlevel.MethodReturnMessage(call)
    if scenario == "reply-signature":
        reply.append(path, signature="s")
    else:
        reply.append(dbus.ObjectPath("/wrong/request" if scenario == "reply-path" else path), signature="o")
    if scenario == "late":
        connection.send_message(reply)
        connection.flush()
    if scenario == "owner-loss":
        connection.send_message(reply)
        connection.flush()
        bus.release_name(portal)
        return True
    if scenario == "unrelated":
        # A unicast signal can arrive without matching our subscription. Ignore the wrong sender and wrong path.
        other = dbus.SessionBus(private=True)
        spoof = dbus.lowlevel.SignalMessage(path, "org.freedesktop.portal.Request", "Response")
        spoof.set_destination(client)
        spoof.append(dbus.UInt32(0), {"color": dbus.Struct((1.0, 0.0, 0.0), signature="ddd", variant_level=1)}, signature="ua{sv}")
        other.send_message(spoof)
        spoof_owner = dbus.lowlevel.SignalMessage("/org/freedesktop/DBus", "org.freedesktop.DBus", "NameOwnerChanged")
        spoof_owner.set_destination(client)
        spoof_owner.append(portal, bus.get_unique_name(), "", signature="sss")
        other.send_message(spoof_owner)
        other.flush()
        other.close()
        unrelated = dbus.lowlevel.SignalMessage(path + "_other", "org.freedesktop.portal.Request", "Response")
        unrelated.set_destination(client)
        unrelated.append(dbus.UInt32(1), {}, signature="ua{sv}")
        connection.send_message(unrelated)
    if scenario != "disconnect":
        response = dbus.lowlevel.SignalMessage(path, "org.freedesktop.portal.Request", "Response")
        response.set_destination(call.get_sender())
        status = 1 if scenario == "cancel" else 2 if scenario == "failure" else 0
        color = dbus.Struct((0.0, 0.5, 1.0), signature="ddd", variant_level=1)
        if scenario == "nan":
            color = dbus.Struct((float("nan"), 0.5, 1.0), signature="ddd", variant_level=1)
        elif scenario == "range":
            color = dbus.Struct((-0.1, 0.5, 1.0), signature="ddd", variant_level=1)
        elif scenario == "infinity":
            color = dbus.Struct((0.0, float("inf"), 1.0), signature="ddd", variant_level=1)
        elif scenario == "upper-range":
            color = dbus.Struct((0.0, 0.5, 1.01), signature="ddd", variant_level=1)
        elif scenario == "short":
            color = dbus.Struct((0.0, 0.5), signature="dd", variant_level=1)
        elif scenario == "long":
            color = dbus.Struct((0.0, 0.5, 1.0, 0.0), signature="dddd", variant_level=1)
        elif scenario == "component-type":
            color = dbus.Struct((0.0, 0.5, dbus.Int32(1)), signature="ddi", variant_level=1)
        elif scenario == "type":
            color = dbus.String("wrong", variant_level=1)
        results = {"color": color, "future-key": dbus.String("ignored", variant_level=1)}
        if scenario in ("cancel", "failure", "missing"):
            results = {}
        if scenario == "signature":
            response.append("wrong", results, signature="sa{sv}")
        else:
            response.append(dbus.UInt32(status), results, signature="ua{sv}")
        connection.send_message(response)
    if scenario != "late":
        connection.send_message(reply)
    connection.flush()
    if scenario == "disconnect":
        GLib.idle_add(loop.quit)
    return True


bus.add_message_filter(handle)
print(os.environ["DBUS_SESSION_BUS_ADDRESS"], flush=True)
loop.run()
bus.close()
