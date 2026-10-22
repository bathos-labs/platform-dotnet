"""Private file-manager protocol fixture. Never contacts a desktop file manager."""
import os
import sys

import dbus
import dbus.lowlevel
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib

DBusGMainLoop(set_as_default=True)
bus = dbus.SessionBus(private=True)
assert bus.request_name("org.freedesktop.FileManager1", dbus.bus.NAME_FLAG_DO_NOT_QUEUE) == dbus.bus.REQUEST_NAME_REPLY_PRIMARY_OWNER
loop = GLib.MainLoop()
scenario, expected_uri = sys.argv[1:]
client = None
bus.add_match_string("type='signal',sender='org.freedesktop.DBus',interface='org.freedesktop.DBus',member='NameOwnerChanged'")


def handle(connection, call):
    global client
    if call.get_interface() == "org.freedesktop.DBus" and call.get_member() == "NameOwnerChanged":
        name, previous, current = call.get_args_list()
        if name == client and not current:
            loop.quit()
        return
    if call.get_interface() != "org.freedesktop.FileManager1" or call.get_member() != "ShowItems":
        return
    client = call.get_sender()
    assert call.get_signature() == "ass"
    uris, startup = call.get_args_list()
    assert list(uris) == [expected_uri]
    assert startup == ""
    if scenario == "error":
        reply = dbus.lowlevel.ErrorMessage(call, "org.freedesktop.DBus.Error.NotSupported", "No file manager")
    else:
        reply = dbus.lowlevel.MethodReturnMessage(call)
        if scenario == "malformed":
            reply.append(dbus.UInt32(42), signature="u")
    connection.send_message(reply)
    connection.flush()
    return True


bus.add_message_filter(handle)
print(os.environ["DBUS_SESSION_BUS_ADDRESS"], flush=True)
loop.run()
bus.close()
