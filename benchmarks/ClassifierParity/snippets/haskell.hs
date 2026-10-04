module Main where
main :: IO ()
main = mapM_ print (take 5 (iterate (* 2) 1))
