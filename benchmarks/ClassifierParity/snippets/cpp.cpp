#include <vector>
#include <iostream>
template <typename T> T sum(const std::vector<T>& v) { T s{}; for (auto& x : v) s += x; return s; }
int main() { std::cout << sum(std::vector<int>{1, 2, 3}) << std::endl; }
