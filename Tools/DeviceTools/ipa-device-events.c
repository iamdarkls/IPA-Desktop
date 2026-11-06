/* IPA Desktop device change bridge. MIT, see repository LICENSE.txt.
 * No UDIDs or device data cross this pipe. EOF on stdin releases the subscription. */
#include <stdio.h>
#include <string.h>
#include <libimobiledevice/libimobiledevice.h>

static void changed(const idevice_event_t *event, void *context)
{
    (void)event; (void)context;
    puts("changed");
    fflush(stdout);
}

int main(int argc, char **argv)
{
    if (argc == 2 && (!strcmp(argv[1], "--version") || !strcmp(argv[1], "--help"))) {
        puts("IPA-DESKTOP-DEVICE-EVENTS/1");
        return 0;
    }
    if (argc != 1) return 2;
    idevice_subscription_context_t subscription = NULL;
    if (idevice_events_subscribe(&subscription, changed, NULL) != IDEVICE_E_SUCCESS) return 1;
    puts("ready"); fflush(stdout);
    while (getchar() != EOF) { }
    return idevice_events_unsubscribe(subscription) == IDEVICE_E_SUCCESS ? 0 : 1;
}
