"""给发布包生成校验和与 Ed25519 签名。

用法：python scripts/sign-release.py guage-1.0.4.zip

私钥放在 tools/keys/updater-private.key（32 字节二进制，已被 .gitignore 忽略）。
生成的两个文件要和 zip 一起上传到 release，客户端才认。
"""
import base64
import hashlib
import os
import sys

from nacl.signing import SigningKey

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
KEY = os.path.join(ROOT, "tools", "keys", "updater-private.key")


def main():
    if len(sys.argv) != 2:
        print("用法：python scripts/sign-release.py <zip 路径>")
        return 1

    pkg = sys.argv[1]
    if not os.path.isfile(pkg):
        print("找不到文件：" + pkg)
        return 1
    if not os.path.isfile(KEY):
        print("缺少私钥：" + KEY)
        return 1

    name = os.path.basename(pkg)
    digest = hashlib.sha256()
    with open(pkg, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            digest.update(chunk)

    sha_path = pkg + ".sha256"
    with open(sha_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("%s  %s\n" % (digest.hexdigest(), name))

    with open(KEY, "rb") as f:
        key = SigningKey(f.read())
    with open(sha_path, "rb") as f:
        signature = key.sign(f.read()).signature
    with open(sha_path + ".sig", "w", encoding="utf-8") as f:
        f.write(base64.b64encode(signature).decode())

    print("已生成：")
    print("  " + sha_path)
    print("  " + sha_path + ".sig")
    return 0


if __name__ == "__main__":
    sys.exit(main())
