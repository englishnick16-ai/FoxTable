#!/usr/bin/env python3
"""Verify FoxTable's packaged native libraries and actual DEX class definitions.

Usage: python3 Tools/verify_apk.py /path/to/FoxTable.apk
This checks packaging, not Android installation or interactive runtime behavior.
"""
import hashlib
import json
from pathlib import Path
import struct
import sys
import zipfile


def defined_classes(data):
    if not data.startswith(b'dex\n') or len(data) < 112:
        raise ValueError('Invalid DEX header')
    if struct.unpack_from('<I', data, 40)[0] != 0x12345678:
        raise ValueError('Unsupported DEX byte order')
    string_count, string_offset, type_count, type_offset = struct.unpack_from('<4I', data, 56)
    class_count, class_offset = struct.unpack_from('<2I', data, 96)
    for index in range(class_count):
        class_index = struct.unpack_from('<I', data, class_offset + index * 32)[0]
        if class_index >= type_count:
            raise ValueError('Invalid DEX class index')
        descriptor_index = struct.unpack_from('<I', data, type_offset + class_index * 4)[0]
        if descriptor_index >= string_count:
            raise ValueError('Invalid DEX descriptor index')
        start = struct.unpack_from('<I', data, string_offset + descriptor_index * 4)[0]
        for _ in range(5):
            value = data[start]
            start += 1
            if not value & 0x80:
                break
        else:
            raise ValueError('Invalid DEX string length')
        end = data.index(0, start)
        yield data[start:end].decode('utf-8')


def verify(path):
    with zipfile.ZipFile(path) as archive:
        broken = archive.testzip()
        if broken:
            raise ValueError('ZIP CRC failed: ' + broken)
        names = set(archive.namelist())
        required = {'AndroidManifest.xml', 'classes.dex',
                    'lib/arm64-v8a/libunity.so', 'lib/arm64-v8a/libil2cpp.so',
                    'assets/bin/Data/Managed/Metadata/global-metadata.dat'}
        if missing := required - names:
            raise ValueError('Missing APK entries: ' + ', '.join(sorted(missing)))
        classes = set()
        for name in names:
            if name.startswith('classes') and name.endswith('.dex'):
                classes.update(defined_classes(archive.read(name)))
        if 'Lcom/foxtable/FoxGallery;' not in classes:
            raise ValueError('FoxGallery activity is not defined in DEX; Android map picker cannot open')
        metadata = archive.read('assets/bin/Data/Managed/Metadata/global-metadata.dat')
        if b'OnAndroidImage\0' not in metadata:
            raise ValueError('Android image callback is missing from IL2CPP metadata')
        abis = sorted({name.split('/')[1] for name in names if name.startswith('lib/') and name.endswith('.so')})
        return {'file': str(path), 'size_bytes': path.stat().st_size,
                'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                'crc_ok': True, 'abis': abis, 'gallery_class_defined': True,
                'android_callback_metadata': True, 'dex_class_count': len(classes)}


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise SystemExit('Usage: verify_apk.py /path/to/FoxTable.apk')
    try:
        print(json.dumps(verify(Path(sys.argv[1])), indent=2))
    except (ValueError, KeyError, OSError, struct.error, zipfile.BadZipFile) as error:
        raise SystemExit(str(error))
