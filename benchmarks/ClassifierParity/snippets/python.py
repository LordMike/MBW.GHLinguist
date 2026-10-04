import sys
from dataclasses import dataclass

@dataclass
class Item:
    name: str
    count: int = 0

def main(argv: list[str]) -> int:
    print([Item(a) for a in argv])
    return 0
