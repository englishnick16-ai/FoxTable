"""Consistent SQLite backup, including committed WAL transactions."""
import argparse
import sqlite3
from pathlib import Path

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source')
    parser.add_argument('destination')
    args = parser.parse_args()
    source, destination = Path(args.source).resolve(), Path(args.destination).resolve()
    if not source.is_file():
        parser.error('Source database does not exist')
    if destination.exists() or destination == source:
        parser.error('Destination must be a new file')
    # Read-only URI avoids creating an empty source by mistake.
    with sqlite3.connect(source.as_uri()+'?mode=ro', uri=True) as db:
        with sqlite3.connect(destination) as target:
            db.backup(target)
    print('Backup created:', destination)
