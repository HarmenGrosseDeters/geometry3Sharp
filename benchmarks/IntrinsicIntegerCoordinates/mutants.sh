#!/bin/bash
# Do the checks of the prototype have teeth? Applies deliberate errors to NormalCoordinates.cs, one at a time,
# runs the integer stage and prints how each is caught (exit code 0 = not caught). Restores the file.
#   bash benchmarks/IntrinsicIntegerCoordinates/mutants.sh > benchmarks/IntrinsicIntegerCoordinates/results/mutants.txt
cd "$(dirname "$0")" || exit 1
cp NormalCoordinates.cs NormalCoordinates.cs.orig
# (touch: the restored file must be newer than the last mutant build)
trap 'mv NormalCoordinates.cs.orig NormalCoordinates.cs; touch NormalCoordinates.cs' EXIT
names=("flip count: one emanating term dropped" "flip roundabout: shared edge not counted" "insertion count off by one"
  "trace rule: corner test off by one" "insertion region test inverted" "unfolding to the wrong side"
  "positions given to the wrong crossings" "band quads wound the other way" "fan crossings in reverse order")
seds=('s/- eL - eK/- eL/' 's/rki + Emanating(tb) + Neg(nki)/rki + Emanating(tb)/'
  's/np\[fan\] = 0; np\[k1\] = c\[k1\] + lo;/np[fan] = 0; np[k1] = c[k1] + lo + 1;/'
  's/if (p < ca) {/if (p <= ca \&\& ca > 0) {/'
  's/Orient(X, Y, P) \* Orient(X, Y, q\[k\]) < 0/Orient(X, Y, P) * Orient(X, Y, q[k]) > 0/'
  's/T.Length(Next(tw)), T.Length(Prev(tw)), -1)/T.Length(Next(tw)), T.Length(Prev(tw)), +1)/'
  's/S.Points\[Id(baseId, list\[k\])\] = a + s \* (b - a);/S.Points[Id(baseId, list[list.Count - 1 - k])] = a + s * (b - a);/'
  's/S.Polygons.Add(new\[\] { x0, x1, y1, y0 });/S.Polygons.Add(new[] { x0, y0, y1, x1 });/'
  's/P = c + m }));/P = c + e - 1 - m }));/')
for i in "${!seds[@]}"; do
  cp NormalCoordinates.cs.orig NormalCoordinates.cs
  sed -i "${seds[$i]}" NormalCoordinates.cs
  if cmp -s NormalCoordinates.cs NormalCoordinates.cs.orig; then echo "mutant $((i+1)) (${names[$i]}): NOT APPLIED"; continue; fi
  out=$(dotnet run -c Release --project . -- integer 2>&1); code=$?
  first=$(echo "$out" | grep -v '^|' | grep -m1 -i 'exception\|integer:\|differ')
  echo "mutant $((i+1)) (${names[$i]}): exit code $code; invalid runs reported: $(echo "$out" | grep -c ' integer: '); first message: ${first:-none}"
done
