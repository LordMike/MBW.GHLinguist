use strict;
use warnings;
my %seen;
while (my $line = <STDIN>) { chomp $line; print "$line\n" unless $seen{$line}++; }
