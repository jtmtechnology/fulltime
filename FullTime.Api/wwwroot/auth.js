// Shared cross-page auth helpers. Page-specific form handling stays inline in each page's
// own <script> block — this file is only for the bits every page needs.
const TOKEN_KEY = 'fulltime_token';

function getToken() {
  return localStorage.getItem(TOKEN_KEY);
}

function setToken(token) {
  localStorage.setItem(TOKEN_KEY, token);
}

function clearToken() {
  localStorage.removeItem(TOKEN_KEY);
}

function isLoggedIn() {
  return !!getToken();
}

function decodeToken() {
  const token = getToken();
  if (!token) return null;
  try {
    const payload = token.split('.')[1];
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    return JSON.parse(json);
  } catch {
    return null;
  }
}

async function authFetch(url, options = {}) {
  const token = getToken();
  const headers = { ...(options.headers || {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) };
  const res = await fetch(url, { ...options, headers });
  if (res.status === 401) {
    clearToken();
    window.location.href = 'login.html';
  }
  return res;
}

function logout() {
  clearToken();
  window.location.href = 'login.html';
}

function requireAuth() {
  if (!isLoggedIn()) {
    window.location.href = 'login.html';
  }
}

function renderNav(containerId) {
  const el = document.getElementById(containerId);
  if (!el) return;

  const payload = decodeToken();
  if (payload) {
    el.innerHTML = `Hi ${payload.name} · <a href="index.html">Matches</a> · <a href="mybets.html">My Bets</a> · <a href="leaderboard.html">Leaderboard</a> · <a href="profile.html">Profile</a> · <a href="#" id="logout-link">Logout</a>`;
    document.getElementById('logout-link').addEventListener('click', (e) => {
      e.preventDefault();
      logout();
    });
  } else {
    el.innerHTML = `<a href="login.html">Login</a> · <a href="register.html">Register</a>`;
  }
}

document.addEventListener('DOMContentLoaded', () => renderNav('auth-nav'));
