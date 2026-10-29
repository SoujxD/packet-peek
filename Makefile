.PHONY: c-test
c-test:
	cmake -S c -B build/c -DBUILD_TESTING=ON $(CMAKE_ARGS)
	cmake --build build/c
	ctest --test-dir build/c --output-on-failure
