<?php
// GoldataWeb2 P0 security hot-fix (002-spec §11, NFR-SEC-2): endpoints that had NO login check.
// Usage at the very top of a script (after `namespace` if the file has one):
//   require_once __DIR__ . '/require_login.php';                       // any logged-in user
//   require_login(['管理'], '总监');                                    // role(s) + rank
// Answers 401/403 as plain text (these are AJAX/form endpoints, not pages).

if (session_status() === PHP_SESSION_NONE) {
    session_start();
}

function require_login(array $roles = [], ?string $rank = null): void
{
    $timeout = 9600;   // same as check_timeout() in login.php
    $ok = !empty($_SESSION['username']) && isset($_SESSION['timeout']) && (time() - (int)$_SESSION['timeout']) <= $timeout;
    if (!$ok) {
        http_response_code(401);
        header('Content-Type: text/plain; charset=utf-8');
        exit('请先登入');
    }
    if (function_exists('app_auth_revalidate')) {
        app_auth_revalidate();
    }
    $role_ok = $roles === [] || in_array($_SESSION['role'] ?? '', $roles, true);
    $rank_ok = $rank === null || ($_SESSION['rank'] ?? '') === $rank;
    $read_only = substr((string)($_SESSION['role'] ?? ''), -strlen('查阅')) === '查阅';
    if (!$role_ok || !$rank_ok) {
        http_response_code(403);
        header('Content-Type: text/plain; charset=utf-8');
        exit('权限不足');
    }
    $GLOBALS['goldata_read_only'] = $read_only;
}

/** Blocks the read-only (*查阅) roles from a write endpoint. */
function require_write(): void
{
    if (!empty($GLOBALS['goldata_read_only'])) {
        http_response_code(403);
        header('Content-Type: text/plain; charset=utf-8');
        exit('只读账户不能修改数据');
    }
}

require_once __DIR__ . '/app_auth.php';
