loop "ambience/forest":
    volume = 0.6to0.8
    move = pos, 0, 0, -3
loop "ambience/forest":
    volume = 0.25to0.4
    move = pos, -12, 0, -6
loop "ambience/forest":
    volume = 0.25to0.4
    move = pos, 12, 0, -6
7 * oneshot "bird/1to7" every 3to7:
    volume = 0.08to0.22
    move = fly, -20to20, 6to18, -12to6, 0.6to1.2
    visual = trail
3 * oneshot "footsteps/1to36" every 12to24:
    volume = 0.15to0.35
    move = walk, -18to18, -2to10, 0.6to1.1
    visual = sphere
oneshot "conversation/people" every 18to35:
    volume = 0.12to0.28
    move = pos, -10to10, 0, 8to14
    visual = "sphere and trail"