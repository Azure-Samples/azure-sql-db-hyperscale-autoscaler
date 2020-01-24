WITH cte AS
(
	SELECT
		CASE WHEN ([Number] % 2) = 1 THEN 1 ELSE 0 END AS GroupId,
		[Number],
		Random
	FROM
		dbo.[Numbers]
	WHERE
		[Number] BETWEEN 1 AND 300000
)
SELECT
	GroupId,
	COUNT(*),
	AVG(Random),
	STDEV(Random)
FROM
	cte
GROUP BY
	GroupId