loop "music/beat":
    volume = 0.12
    visual = sphere
2 * oneshot "bicycle/1to37" every 6to12:
    volume = 0.5to0.9
    move = walk, -30to30, -20to20, 1.1
    visual = trail