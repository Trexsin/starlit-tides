# Starlit Tides - build file
#   make        build
#   make run    build and play
#   make clean  remove the binary

TARGET   := starlit_tides
SRC      := starlit_tides.cpp
CXXFLAGS := -std=c++11 -O2 -Wall -Wextra

ifeq ($(OS),Windows_NT)
    # MinGW + freeglut
    CXX    ?= g++
    TARGET := starlit_tides.exe
    LIBS   := -lfreeglut -lglu32 -lopengl32
else
    UNAME := $(shell uname -s)
    ifeq ($(UNAME),Darwin)
        CXX  := clang++
        LIBS := -framework OpenGL -framework GLUT
    else
        # Linux: sudo apt install freeglut3-dev
        LIBS := -lglut -lGLU -lGL -lm
    endif
endif

$(TARGET): $(SRC)
	$(CXX) $(CXXFLAGS) $(SRC) -o $(TARGET) $(LIBS)

run: $(TARGET)
	./$(TARGET)

clean:
	rm -f $(TARGET) starlit_tides.exe

.PHONY: run clean
