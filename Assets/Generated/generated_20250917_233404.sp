loop "music/beat":
    volume = 0.1to0.2
4 * oneshot "bicycle/1to37" every 7to12:
    volume = 0.3to0.6
    move = walk, -30to30, -30to30, 1.3
    visual = trail