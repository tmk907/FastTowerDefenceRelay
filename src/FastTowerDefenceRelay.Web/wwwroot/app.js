"use strict";

const REFRESH_INTERVAL_MS = 10_000;
const NOTIFICATIONS_KEY = "ftd-relay.notifications";

const elements = {
  protocol: document.getElementById("protocol"),
  rooms: document.getElementById("rooms"),
  roomRows: document.querySelector("#rooms tbody"),
  empty: document.getElementById("empty"),
  status: document.getElementById("status"),
  notifications: document.getElementById("notifications"),
  notificationHint: document.getElementById("notification-hint"),
};

// Null until the first successful load, so lobbies open at page load do not notify.
let knownRoomIds = null;

// Whether the viewer turned notifications on; the browser permission is checked separately.
let notificationsWanted = readStoredPreference();

async function refresh() {
  try {
    // Relative, so the page also works behind a reverse proxy path prefix.
    const response = await fetch("rooms", { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }
    const listing = await response.json();
    render(listing);
    notifyAboutNewRooms(listing.rooms);
    setStatus(`Updated at ${new Date().toLocaleTimeString()}. Refreshes every ${REFRESH_INTERVAL_MS / 1000} seconds.`);
  } catch (error) {
    setStatus(`Could not load lobbies (${error.message}). Retrying in ${REFRESH_INTERVAL_MS / 1000} seconds.`, true);
  } finally {
    setTimeout(refresh, REFRESH_INTERVAL_MS);
  }
}

function render(listing) {
  elements.protocol.textContent = `${listing.protocol.major}.${listing.protocol.minor}`;

  // Names come from players: insert them as text, never as HTML.
  const rows = listing.rooms.map((room) => {
    const row = document.createElement("tr");
    const players = cell(`${room.players} / ${room.capacity}`, "number");
    if (room.players >= room.capacity) {
      players.classList.add("full");
    }
    row.append(cell(room.name), cell(room.host), players, cell(String(room.id), "number"));
    return row;
  });
  elements.roomRows.replaceChildren(...rows);
  elements.rooms.hidden = rows.length === 0;
  elements.empty.hidden = rows.length !== 0;
}

function cell(text, className) {
  const element = document.createElement("td");
  element.textContent = text;
  if (className) {
    element.className = className;
  }
  return element;
}

function setStatus(text, isError = false) {
  elements.status.textContent = text;
  elements.status.classList.toggle("error", isError);
}

function notifyAboutNewRooms(rooms) {
  const previous = knownRoomIds;
  knownRoomIds = new Set(rooms.map((room) => room.id));
  if (previous === null || !notificationsEnabled()) {
    return;
  }
  for (const room of rooms) {
    if (!previous.has(room.id)) {
      new Notification(`New lobby: ${room.name}`, {
        body: `Hosted by ${room.host} · ${room.players} / ${room.capacity} players`,
        tag: `ftd-room-${room.id}`,
      });
    }
  }
}

function notificationsSupported() {
  return "Notification" in window && window.isSecureContext;
}

function notificationsEnabled() {
  return notificationsSupported() && Notification.permission === "granted" && notificationsWanted;
}

// Storage can be unavailable, for example in some private windows;
// the choice then lasts until the page is reloaded.
function readStoredPreference() {
  try {
    return localStorage.getItem(NOTIFICATIONS_KEY) === "on";
  } catch {
    return false;
  }
}

function setNotificationsWanted(wanted) {
  notificationsWanted = wanted;
  try {
    localStorage.setItem(NOTIFICATIONS_KEY, wanted ? "on" : "off");
  } catch {
    // Kept in memory only.
  }
}

function updateNotificationButton() {
  const button = elements.notifications;
  const hint = elements.notificationHint;
  button.hidden = false;
  hint.hidden = true;

  if (!notificationsSupported()) {
    button.disabled = true;
    button.textContent = "Notifications unavailable";
    hint.textContent = window.isSecureContext
      ? "This browser does not support notifications."
      : "Notifications need the page to be opened over HTTPS.";
    hint.hidden = false;
    return;
  }
  if (Notification.permission === "denied") {
    button.disabled = true;
    button.textContent = "Notifications blocked";
    hint.textContent = "Allow notifications for this site in the browser settings to enable them.";
    hint.hidden = false;
    return;
  }

  const enabled = notificationsEnabled();
  button.disabled = false;
  button.textContent = enabled ? "Disable notifications" : "Enable notifications";
  button.setAttribute("aria-pressed", String(enabled));
  if (enabled) {
    hint.textContent = "You get a notification when a new lobby opens while this page is open.";
    hint.hidden = false;
  }
}

async function toggleNotifications() {
  if (notificationsEnabled()) {
    setNotificationsWanted(false);
  } else {
    const permission = await Notification.requestPermission();
    setNotificationsWanted(permission === "granted");
  }
  updateNotificationButton();
}

elements.notifications.addEventListener("click", toggleNotifications);
updateNotificationButton();
refresh();
