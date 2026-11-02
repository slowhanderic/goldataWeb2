<?php
// GoldataWeb2 PHP login bridge (REQ-CMP-8, 002-spec §5.5).
// The legacy app reads its users, roles and ranks from goldata_app (the same list the new admin page manages).
// Included by login.php; replaces the hard-coded accounts and password hashes that used to live there.

// Config OUTSIDE the web root, readable by `http` only. It is GENERATED from the environment's .env file by
// `goldata.sh php-config` (keys: host, port, db, user, pass, public_url) — one source of truth for all settings.
// A UAT copy of the PHP site can point elsewhere by defining GOLDATA_AUTH_CONFIG before including login.php.
if (!defined('GOLDATA_AUTH_CONFIG')) {
    define('GOLDATA_AUTH_CONFIG', '/volume1/web_config/goldata_auth.php');
}
const GOLDATA_LOCKOUT_ATTEMPTS = 5;
const GOLDATA_LOCKOUT_MINUTES = 15;
const GOLDATA_REVALIDATE_SECONDS = 60;

function app_auth_config(): array
{
    static $cfg = null;
    if ($cfg === null) {
        $cfg = is_readable(GOLDATA_AUTH_CONFIG) ? require GOLDATA_AUTH_CONFIG : [];
    }
    return $cfg;
}

function app_auth_db(): mysqli
{
    static $db = null;
    if ($db instanceof mysqli) {
        return $db;
    }
    $cfg = app_auth_config();
    if (empty($cfg['user'])) {
        throw new RuntimeException('goldata_auth config missing: ' . GOLDATA_AUTH_CONFIG);
    }
    mysqli_report(MYSQLI_REPORT_ERROR | MYSQLI_REPORT_STRICT);
    $db = new mysqli($cfg['host'] ?? '127.0.0.1', $cfg['user'], $cfg['pass'] ?? '', $cfg['db'] ?? 'goldata_app', (int)($cfg['port'] ?? 3307));
    $db->set_charset('utf8mb4');
    return $db;
}

/** URL of the new app's change-password page, for the navbar banner. */
function app_auth_change_password_url(): string
{
    $base = rtrim(app_auth_config()['public_url'] ?? '', '/');
    return $base === '' ? '#' : $base . '/change-password';
}

function app_auth_audit(mysqli $db, string $login, bool $success, ?string $reason): void
{
    $st = $db->prepare('INSERT INTO app_login_audit (login, success, source, reason, ip) VALUES (?, ?, \'php\', ?, ?)');
    $ok = $success ? 1 : 0;
    $ip = $_SERVER['REMOTE_ADDR'] ?? null;
    $login = mb_substr($login, 0, 64);
    $st->bind_param('siss', $login, $ok, $reason, $ip);
    $st->execute();
}

/**
 * Same contract as the old check_user(): returns the display name on success, 'nobody' on failure,
 * and fills $_SESSION['valid' | 'timeout' | 'username' | 'role' | 'rank'] exactly as before.
 */
function check_user($user_name, $password)
{
    try {
        $db = app_auth_db();
    } catch (Throwable $e) {
        error_log('GoldataWeb2 login bridge: ' . $e->getMessage());
        echo "<script>alert('登入服务暂不可用，请稍后再试或联系管理员。');</script>";
        return 'nobody';
    }

    $login = strtolower(trim((string)$user_name));
    $st = $db->prepare('SELECT id, login, display_name, role, `rank`, password_hash, is_active, locked_until, failed_logins,
                               must_change_password, security_stamp FROM v_legacy_login WHERE login = ?');
    $st->bind_param('s', $login);
    $st->execute();
    $u = $st->get_result()->fetch_assoc();

    // Unknown logins spend the same time on a bcrypt check (no user enumeration by timing).
    $hash = $u['password_hash'] ?? password_hash(bin2hex(random_bytes(8)), PASSWORD_BCRYPT, ['cost' => 12]);
    $password_ok = password_verify((string)$password, $hash);
    $locked = $u && $u['locked_until'] !== null && strtotime($u['locked_until']) > time();

    $reason = null;
    if (!$u) {
        $reason = 'unknown_login';
    } elseif (!(int)$u['is_active']) {
        $reason = 'disabled';
    } elseif ($locked) {
        $reason = 'locked';
    } elseif (!$password_ok) {
        $reason = 'bad_password';
    }

    $id = $u ? (int)$u['id'] : 0;
    if ($reason === 'bad_password') {
        $failed = (int)$u['failed_logins'] + 1;
        if ($failed >= GOLDATA_LOCKOUT_ATTEMPTS) {
            $q = $db->prepare('UPDATE app_users SET failed_logins = 0, locked_until = DATE_ADD(NOW(), INTERVAL ' . GOLDATA_LOCKOUT_MINUTES . ' MINUTE) WHERE id = ?');
            $q->bind_param('i', $id);
        } else {
            $q = $db->prepare('UPDATE app_users SET failed_logins = ? WHERE id = ?');
            $q->bind_param('ii', $failed, $id);
        }
        $q->execute();
    } elseif ($reason === null) {
        $q = $db->prepare('UPDATE app_users SET failed_logins = 0, locked_until = NULL, last_login_at = NOW() WHERE id = ?');
        $q->bind_param('i', $id);
        $q->execute();
    }
    app_auth_audit($db, $login, $reason === null, $reason);

    if ($reason !== null) {
        if ($reason === 'locked') {
            echo "<script>alert('登入失败次数过多，账户已暂时锁定，请稍后再试或联系管理员。');</script>";
        }
        return 'nobody';
    }

    session_regenerate_id(true);
    $_SESSION['valid'] = true;
    $_SESSION['timeout'] = time();
    $_SESSION['username'] = $u['display_name'];
    $_SESSION['role'] = $u['role'];
    $_SESSION['rank'] = $u['rank'];
    $_SESSION['app_user_id'] = $id;
    $_SESSION['app_stamp'] = $u['security_stamp'];
    $_SESSION['app_checked'] = time();
    $_SESSION['app_must_change'] = (int)$u['must_change_password'];
    return $u['display_name'];
}

function app_auth_kick(string $message): void
{
    session_unset();
    session_destroy();
    if (!headers_sent()) {
        header('Location: index.php');
    }
    echo '<script>alert(' . json_encode($message, JSON_UNESCAPED_UNICODE) . ');location.href="index.php";</script>';
    exit();
}

/**
 * Called at the top of check_session(): at most once a minute, re-reads the user so that disable / delete /
 * password reset in the new admin page end the PHP session, and role/rank changes apply without logging out.
 */
function app_auth_revalidate(): void
{
    if (empty($_SESSION['username'])) {
        return;
    }
    if (empty($_SESSION['app_user_id'])) {
        app_auth_kick('系统已升级，请重新登入');   // session from before the bridge
    }
    if (time() - (int)($_SESSION['app_checked'] ?? 0) < GOLDATA_REVALIDATE_SECONDS) {
        return;
    }
    try {
        $db = app_auth_db();
        $st = $db->prepare('SELECT display_name, role, `rank`, is_active, locked_until, must_change_password, security_stamp FROM v_legacy_login WHERE id = ?');
        $id = (int)$_SESSION['app_user_id'];
        $st->bind_param('i', $id);
        $st->execute();
        $u = $st->get_result()->fetch_assoc();
    } catch (Throwable $e) {
        error_log('GoldataWeb2 revalidate: ' . $e->getMessage());
        return;   // DB hiccup: keep the session, try again next request
    }
    $locked = $u && $u['locked_until'] !== null && strtotime($u['locked_until']) > time();
    if (!$u || !(int)$u['is_active'] || $locked || $u['security_stamp'] !== ($_SESSION['app_stamp'] ?? '')) {
        app_auth_kick('账户已停用或密码已重置，请重新登入');
    }
    $_SESSION['username'] = $u['display_name'];
    $_SESSION['role'] = $u['role'];
    $_SESSION['rank'] = $u['rank'];
    $_SESSION['app_must_change'] = (int)$u['must_change_password'];
    $_SESSION['app_checked'] = time();
}

/** Navbar banner while the user still has the imported/initial password. */
function app_auth_banner(): string
{
    if (empty($_SESSION['app_must_change'])) {
        return '';
    }
    $url = htmlspecialchars(app_auth_change_password_url());
    return '<span class="navbar-text text-warning ms-2">请到<a class="text-warning" href="' . $url . '" target="_blank">新系统修改密码</a></span>';
}
